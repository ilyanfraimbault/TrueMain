using System.Linq.Expressions;
using Core.Options;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;
using TrueMain.ReadModels.Truemains;
using TrueMain.Services.Truemains;

namespace TrueMain.Services.Champions;

/// <summary>
/// Head-to-head between one Riot account and a champion's mains (issue #528):
/// win rate, KDA, CS/min and gold for both sides over the same queue, patch and
/// lane scope.
///
/// The lookup is deliberately database-only. The account must already exist in
/// <c>riot_accounts</c> — there is no on-demand Riot fetch, so an untracked
/// player resolves to <see cref="ChampionComparisonStatus.UnknownAccount"/> and
/// the caller renders an honest empty state.
///
/// Both columns are computed live from <c>match_participants</c>: CS and gold
/// exist nowhere in the aggregate schema, so there is no pre-aggregated source
/// to read. Retention hard-deletes participants beyond the last few patches,
/// which scopes an unpinned comparison to the games still held — the same
/// window every other live champion panel reads.
/// </summary>
public sealed class ChampionMainsComparisonQueryService(
    TrueMainDbContext db,
    TruemainAccountResolver resolver,
    IOptions<MainAnalysisOptions> options,
    IOptions<ChampionsListOptions> championsOptions,
    IChampionReadCache cache)
    : IChampionMainsComparisonQueryService
{
    public async Task<ChampionMainsComparisonResponse> GetAsync(
        int championId,
        string? account,
        string? target,
        string? position,
        string? patch,
        CancellationToken ct)
    {
        // Canonicalise to major.minor ("16.4.521.123" → "16.4"); null or
        // unparseable input means "every patch we still hold".
        //
        // The MVC controller already ran ChampionQueryParameterNormalizer over
        // this, so on that path the second pass is a no-op (both call the same
        // PatchFilter.Normalize, which is idempotent). It stays
        // for the same reason TruemainAccountResolver parses defensively: the
        // interface is reachable without MVC, and a raw GameVersion arriving
        // here would otherwise be compared against the major.minor LIKE prefix
        // and silently match nothing. Every sibling champion read
        // (scaling / matchups / roam / item-timings / powerspikes / leads /
        // composition) normalises the same way for the same reason — dropping
        // it here alone would make this the one service that trusts its caller.
        var normalizedPatch = PatchFilter.Normalize(patch);

        var minGames = Math.Max(0, championsOptions.Value.MinComparisonGames);

        var playerAccount = await resolver.ResolveAsync(account, ct);
        if (playerAccount is null)
        {
            return UnknownAccount(championId, normalizedPatch, position, minGames);
        }

        var targetRequested = !string.IsNullOrWhiteSpace(target);
        var targetAccount = targetRequested ? await resolver.ResolveAsync(target, ct) : null;
        var targetMissing = targetRequested && targetAccount is null;

        // Key the cache on the *resolved* account ids rather than the raw text,
        // so casing variants of the same Riot ID share one entry. An unresolved
        // target gets its own token: its response holds the player column only,
        // so it must never be served for a pool comparison (or vice versa).
        var targetKey = targetAccount?.Id.ToString() ?? (targetMissing ? "unknown" : "pool");
        var cacheKey = $"champions:mains-comparison:{championId}:{position ?? "all"}:{normalizedPatch ?? "all"}"
                       + $":{playerAccount.Id}:{targetKey}";
        return await cache.GetOrComputeAsync(
            cacheKey,
            token => ComputeAsync(
                championId, position, normalizedPatch, minGames,
                playerAccount, targetAccount, targetMissing, token),
            ct);
    }

    /// <summary>
    /// Both columns, once the accounts are resolved. Split out so the resolution —
    /// which decides the cache key — happens before the cache lookup, and everything
    /// after it runs once per (champion, lane, patch, player, target) per aggregation
    /// version no matter how many callers ask at the same time.
    /// </summary>
    private async Task<ChampionMainsComparisonResponse> ComputeAsync(
        int championId,
        string? position,
        string? normalizedPatch,
        int minGames,
        TruemainAccountRef playerAccount,
        TruemainAccountRef? targetAccount,
        bool targetMissing,
        CancellationToken ct)
    {
        var queueId = (int)options.Value.QueueId;
        var playerTotals = await AggregateAsync(
            championId,
            position,
            queueId,
            normalizedPatch,
            p => p.RiotAccountId == playerAccount.Id,
            ct);
        var player = ToSide(playerTotals, Identity(playerAccount), minGames);

        // A named target we don't hold: the account side is already resolved and
        // aggregated, so return it rather than nulling both columns. Only the
        // yardstick is missing, and Player's contract promises a side for every
        // account we do know.
        if (targetMissing)
        {
            return new ChampionMainsComparisonResponse
            {
                ChampionId = championId,
                Patch = normalizedPatch,
                Position = position,
                MinGames = minGames,
                Status = ChampionComparisonStatus.UnknownTarget,
                Player = player,
            };
        }

        // The mains pool is every tracked main of this champion *except* the
        // account being compared: leaving them in would fold their own games
        // into the yardstick they are measured against, which flatters a thin
        // pool. A targeted comparison narrows to that single main instead.
        var mainsTotals = targetAccount is null
            ? await AggregateAsync(
                championId,
                position,
                queueId,
                normalizedPatch,
                p => p.RiotAccountId != playerAccount.Id
                     && db.RiotAccounts.Any(a =>
                         a.Id == p.RiotAccountId
                         && db.MainChampionStats.Any(m =>
                             m.PlatformId == a.PlatformId
                             && m.Puuid == a.Puuid
                             && m.ChampionId == championId
                             && m.IsMain
                             && m.IsActive)),
                ct)
            : await AggregateAsync(
                championId,
                position,
                queueId,
                normalizedPatch,
                p => p.RiotAccountId == targetAccount.Id,
                ct);

        var mains = ToSide(mainsTotals, targetAccount is null ? null : Identity(targetAccount), minGames);

        return new ChampionMainsComparisonResponse
        {
            ChampionId = championId,
            Patch = normalizedPatch,
            Position = position,
            MinGames = minGames,
            Status = player.SampleMet && mains.SampleMet
                ? ChampionComparisonStatus.Ok
                : ChampionComparisonStatus.InsufficientSample,
            Player = player,
            Mains = mains,
        };
    }

    /// <summary>
    /// Sums one side's games in a single grouped round trip. Grouping by account
    /// (rather than folding everything in SQL) is what yields the distinct-player
    /// count for the aggregate column; every match maps to exactly one row per
    /// account, so re-summing the groups in memory is exact.
    /// </summary>
    private async Task<SideTotals> AggregateAsync(
        int championId,
        string? position,
        int queueId,
        string? normalizedPatch,
        Expression<Func<MatchParticipant, bool>> accountFilter,
        CancellationToken ct)
    {
        // Only tracked rows carry an account, and the untracked players who
        // merely shared a truemain's game are never part of either side.
        var participants = db.MatchParticipants
            .AsNoTracking()
            .Where(p => p.ChampionId == championId && p.RiotAccountId != null)
            .Where(accountFilter);

        if (position is not null)
        {
            participants = participants.Where(p => p.TeamPosition == position);
        }

        var rows = await participants
            .Join(
                db.Matches.Where(m =>
                    m.QueueId == queueId
                    && (normalizedPatch == null || m.Patch == normalizedPatch)),
                participant => participant.MatchId,
                match => match.Id,
                (participant, match) => new
                {
                    AccountId = participant.RiotAccountId!.Value,
                    participant.Win,
                    participant.Kills,
                    participant.Deaths,
                    participant.Assists,
                    participant.GoldEarned,
                    Cs = participant.TotalMinionsKilled + participant.NeutralMinionsKilled,
                    match.GameDurationSeconds,
                })
            .GroupBy(x => x.AccountId)
            .Select(g => new
            {
                Games = g.Count(),
                Wins = g.Sum(x => x.Win ? 1 : 0),
                Kills = g.Sum(x => (long)x.Kills),
                Deaths = g.Sum(x => (long)x.Deaths),
                Assists = g.Sum(x => (long)x.Assists),
                Gold = g.Sum(x => (long)x.GoldEarned),
                Cs = g.Sum(x => (long)x.Cs),
                DurationSeconds = g.Sum(x => (long)x.GameDurationSeconds),
            })
            .ToListAsync(ct);

        return new SideTotals(
            Players: rows.Count,
            Games: rows.Sum(r => r.Games),
            Wins: rows.Sum(r => r.Wins),
            Kills: rows.Sum(r => r.Kills),
            Deaths: rows.Sum(r => r.Deaths),
            Assists: rows.Sum(r => r.Assists),
            Gold: rows.Sum(r => r.Gold),
            Cs: rows.Sum(r => r.Cs),
            DurationSeconds: rows.Sum(r => r.DurationSeconds));
    }

    /// <summary>
    /// The columnless response for an account we don't hold. Only reachable for
    /// <see cref="ChampionComparisonStatus.UnknownAccount"/> — an unresolved
    /// <c>main</c> still returns the player's column, so it does not come
    /// through here. Deliberately uncached: the lookup is one indexed equality,
    /// and caching a negative would keep a freshly-ingested account "unknown"
    /// for the whole TTL.
    /// </summary>
    private static ChampionMainsComparisonResponse UnknownAccount(
        int championId,
        string? patch,
        string? position,
        int minGames) => new()
        {
            ChampionId = championId,
            Patch = patch,
            Position = position,
            MinGames = minGames,
            Status = ChampionComparisonStatus.UnknownAccount,
        };

    private static ProfileIdentityReadModel Identity(TruemainAccountRef account) => new()
    {
        GameName = account.GameName,
        TagLine = account.TagLine,
        PlatformId = account.PlatformId,
        ProfileIconId = account.ProfileIconId,
        SummonerLevel = account.SummonerLevel,
    };

    private static ChampionComparisonSideReadModel ToSide(
        SideTotals totals,
        ProfileIdentityReadModel? identity,
        int minGames)
    {
        var games = totals.Games;
        // Summed durations, not games × average length: a side's long games must
        // weigh proportionally in a per-minute rate.
        var minutes = totals.DurationSeconds / 60d;

        return new ChampionComparisonSideReadModel
        {
            Identity = identity,
            Players = totals.Players,
            Games = games,
            Wins = totals.Wins,
            WinRate = RateMath.Rate(totals.Wins, games),
            Kills = Per(totals.Kills, games),
            Deaths = Per(totals.Deaths, games),
            Assists = Per(totals.Assists, games),
            Kda = RateMath.Kda(totals.Kills, totals.Deaths, totals.Assists, games),
            CsPerMin = Per(totals.Cs, minutes),
            GoldPerMin = Per(totals.Gold, minutes),
            GoldPerGame = Per(totals.Gold, games),
            SampleMet = games > 0 && games >= minGames,
        };
    }

    private static double Per(long total, double denominator)
        => denominator <= 0 ? 0d : total / denominator;

    /// <summary>Raw sums for one column, before any per-game / per-minute division.</summary>
    private sealed record SideTotals(
        int Players,
        int Games,
        int Wins,
        long Kills,
        long Deaths,
        long Assists,
        long Gold,
        long Cs,
        long DurationSeconds);
}
