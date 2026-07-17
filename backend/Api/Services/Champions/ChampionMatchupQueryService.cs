using Core.Lol.Patches;
using Core.Lol.Ranking;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Champion lane-matchups query. The global leaderboard slice is served from the
/// pre-aggregated <c>champion_matchup_stats</c> table (#606): one indexed read,
/// folded to the requested patch scope with the games floor applied on the merged
/// total. The player-scoped and opponent-search slices stay live — they self-join
/// <c>match_participants</c> to pair the champion with its lane opponent (same
/// <c>TeamPosition</c>, opposite <c>TeamId</c>, same match), because they need
/// per-account filtering / a sub-floor the aggregate does not carry.
/// </summary>
public sealed class ChampionMatchupQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IOptions<ChampionsListOptions> championsOptions)
    : IChampionMatchupQueryService
{
    public async Task<ChampionMatchupsResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        Guid? riotAccountId,
        int? opponentChampionId,
        string? eloBracket,
        CancellationToken ct)
    {
        // Canonicalise to major.minor (e.g. "16.4.521.123" → "16.4"). The
        // interface contract accepts either form, so the service normalises its
        // own input and stays correct standalone. Null / unparseable input means
        // "every patch".
        var normalizedPatch = string.IsNullOrWhiteSpace(patch)
            ? null
            : PatchVersion.TryParse(patch, out var parsed) ? parsed.ToMajorMinor() : null;

        // Resolve the elo filter to its bands (null = ALL, no clause). Applied to
        // the champion side on both the aggregate and the live paths.
        var bands = EloBracket.ResolveFilter(eloBracket);

        // The global slice (no player, no opponent) is the only one backed by the
        // aggregate. The other two stay live: an opponent lookup wants the
        // head-to-head from a single game (floor 1), and a player slice filters to
        // one account with a lower floor — neither is expressible against a
        // global, floor-free aggregate.
        var matchups = opponentChampionId is null && riotAccountId is null
            ? await ReadFromAggregateAsync(championId, position, normalizedPatch, bands, ct)
            : await ComputeLiveAsync(championId, position, normalizedPatch, riotAccountId, opponentChampionId, bands, ct);

        return new ChampionMatchupsResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = normalizedPatch,
            Matchups = matchups,
        };
    }

    private async Task<List<ChampionMatchupEntry>> ReadFromAggregateAsync(
        int championId,
        string position,
        string? normalizedPatch,
        IReadOnlyCollection<string>? bands,
        CancellationToken ct)
    {
        var minGames = championsOptions.Value.MinMatchupGames;

        // Rows are stored per (opponent, patch, band) with no floor. Fold to the
        // requested scope — one patch, or every patch summed; the requested elo
        // bands, or every band — then apply the floor on the merged total so the
        // all-patches view floors on the real total, not on any single slice.
        var query = db.ChampionMatchupStats
            .AsNoTracking()
            .Where(s => s.ChampionId == championId && s.TeamPosition == position);
        if (normalizedPatch is not null)
        {
            query = query.Where(s => s.Patch == normalizedPatch);
        }
        if (bands is not null)
        {
            query = query.Where(s => bands.Contains(s.EloBracket));
        }

        var rows = await query
            .GroupBy(s => s.OpponentChampionId)
            .Select(g => new
            {
                Opponent = g.Key,
                Games = g.Sum(x => x.Games),
                Wins = g.Sum(x => x.Wins),
            })
            .Where(x => x.Games >= minGames)
            .ToListAsync(ct);

        return ToOrderedEntries(rows.Select(x => (x.Opponent, x.Games, x.Wins)));
    }

    private async Task<List<ChampionMatchupEntry>> ComputeLiveAsync(
        int championId,
        string position,
        string? normalizedPatch,
        Guid? riotAccountId,
        int? opponentChampionId,
        IReadOnlyCollection<string>? bands,
        CancellationToken ct)
    {
        // Same queue cast the sibling champion reads use, so the matchup slice
        // is drawn from the same population as the build / summary pages.
        var queueId = (int)options.Value.QueueId;

        // The matches table stores the full Riot GameVersion, so an exact compare
        // would never hit; the LIKE prefix bridges normalised input to it.
        var patchPrefix = normalizedPatch is null ? null : $"{normalizedPatch}.%";

        // Floor matrix for the two live slices. A deliberate opponent lookup shows
        // the head-to-head from a single game up (floor 1); a player leaderboard
        // keeps the lower per-player floor.
        var minGames = opponentChampionId is not null
            ? 1
            : championsOptions.Value.MinPlayerMatchupGames;

        // The champion side of the lane: rows for this champion at this
        // position, on the configured queue (matched via the correlated
        // EXISTS over matches), optionally narrowed to one player.
        var championRows = db.MatchParticipants
            .AsNoTracking()
            .Where(p1 => p1.ChampionId == championId && p1.TeamPosition == position)
            .Where(p1 => db.Matches.Any(m =>
                m.Id == p1.MatchId
                && m.QueueId == queueId
                && (normalizedPatch == null
                    || EF.Functions.Like(m.GameVersion, patchPrefix!))));

        // Scope the champion side to tracked accounts so the matchup pool matches
        // the champion page's aggregation, which only counts tracked truemains —
        // never the untracked random players who merely shared a truemain's game.
        // A player-scoped call narrows to one account.
        championRows = riotAccountId is { } accountId
            ? championRows.Where(p1 => p1.RiotAccountId == accountId)
            : championRows.Where(p1 => p1.RiotAccountId != null);

        // Narrow the champion side to the requested elo bands (null = every band).
        if (bands is not null)
        {
            championRows = championRows.Where(p1 => bands.Contains(p1.EloBracket));
        }

        // One SQL round-trip: correlate each champion row to its lane opponent
        // (same match + position, opposite team), group by the opponent
        // champion, and COUNT(*) / SUM(win) per opponent. The minimum-games floor
        // is applied in SQL (HAVING) so thin samples never cross the wire.
        var rows = await championRows
            .SelectMany(
                p1 => db.MatchParticipants.Where(p2 =>
                    p2.MatchId == p1.MatchId
                    && p2.TeamPosition == p1.TeamPosition
                    && p2.TeamId != p1.TeamId
                    && (opponentChampionId == null || p2.ChampionId == opponentChampionId)),
                (p1, p2) => new { Opponent = p2.ChampionId, p1.Win })
            .GroupBy(x => x.Opponent)
            .Select(g => new
            {
                Opponent = g.Key,
                Games = g.Count(),
                Wins = g.Sum(x => x.Win ? 1 : 0),
            })
            .Where(x => x.Games >= minGames)
            .ToListAsync(ct);

        return ToOrderedEntries(rows.Select(x => (x.Opponent, x.Games, x.Wins)));
    }

    /// <summary>
    /// Shared final projection for both read paths: materialised
    /// (opponent, games, wins) rows — every one already above its floor, so
    /// games is never zero — mapped to entries ordered best-winrate first.
    /// </summary>
    private static List<ChampionMatchupEntry> ToOrderedEntries(
        IEnumerable<(int Opponent, int Games, int Wins)> rows)
        => rows
            .Select(x => new ChampionMatchupEntry
            {
                OpponentChampionId = x.Opponent,
                Games = x.Games,
                Wins = x.Wins,
                WinRate = RateMath.Rate(x.Wins, x.Games),
            })
            .OrderByDescending(m => m.WinRate)
            .ToList();
}
