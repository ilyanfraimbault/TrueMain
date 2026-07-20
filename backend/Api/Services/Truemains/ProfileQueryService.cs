using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

public sealed class ProfileQueryService(
    TrueMainDbContext db,
    IDbContextFactory<TrueMainDbContext> dbFactory,
    ILogger<ProfileQueryService> logger) : IProfileQueryService
{
    // Shared with the leaderboard so both views derive a player's mains from the
    // same top-N slice (see MainChampionsPolicy / #521).
    private const int MainChampionsCap = MainChampionsPolicy.Cap;
    private const string Surface = "truemain-profile";

    // Private DTOs used to carry query results out of factory-owned contexts.
    private sealed record SnapshotDto(
        string Tier,
        string Division,
        int LeaguePoints,
        int? Wins,
        int? Losses);

    private sealed record MainDto(
        int ChampionId,
        int ChampionMatches,
        double PlayRate,
        string PrimaryPosition,
        bool IsOtp,
        List<PositionStat> PositionBreakdown);

    public async Task<ProfileReadModel?> GetAsync(string nameTag, CancellationToken ct)
    {
        if (!NameTagParser.TryParse(nameTag, out var parsed))
        {
            return null;
        }

        // Multi-platform name-tag disambiguation: a (gameName, tagLine) pair
        // is unique within a Riot routing region but can collide across
        // regions (rare but real). Picking the most-recently-active row
        // matches what a human looking for "this player" would expect, and
        // mirrors the resolver used by the matches endpoint so both routes
        // always land on the same account.
        var account = await db.RiotAccounts
            .AsNoTracking()
            .Where(a => a.GameName == parsed.GameName && a.TagLine == parsed.TagLine)
            .OrderByDescending(a => a.LastMatchIngestAtUtc ?? a.UpdatedAtUtc)
            .Select(a => new
            {
                a.Id,
                a.Puuid,
                a.GameName,
                a.TagLine,
                a.PlatformId,
                a.ProfileIconId,
                a.SummonerLevel,
            })
            .FirstOrDefaultAsync(ct);

        if (account is null)
        {
            return null;
        }

        // snapshot and mains are both keyed on the resolved account but are
        // otherwise independent of each other, so they can run concurrently.
        // A single DbContext is not thread-safe, so each concurrent branch
        // gets its own short-lived context created from the factory.
        var snapshotTask = FetchSnapshotAsync(account.Id, ct);
        var mainsTask = FetchMainsAsync(account.PlatformId, account.Puuid, ct);

        await Task.WhenAll(snapshotTask, mainsTask);

        var snapshot = await snapshotTask;
        var mains = await mainsTask;

        // Aggregate position breakdown across the top mains. The per-champion
        // entries already sum to that champion's games, so summing the
        // Games across mains gives a fair "where did this player play"
        // distribution scoped to the cards we're showing — not the
        // account's full lifetime, which would over-weight off-role pool
        // champions the analysis chose to drop.
        // Deterministic ordinal tiebreak on Position after Games so the primary
        // lane (this list's first entry, shown by ProfilePositionBreakdown.vue)
        // is stable across requests on an exact games tie — and, crucially,
        // resolves the same way as the leaderboard's ComputePositions so a
        // player's primary/secondary never disagrees between the two views (#521).
        var positionSums = mains
            .SelectMany(m => m.PositionBreakdown)
            .Where(p => !string.IsNullOrWhiteSpace(p.Position))
            .GroupBy(p => p.Position)
            .Select(g => new { Position = g.Key, Games = g.Sum(x => x.Games) })
            .OrderByDescending(p => p.Games)
            .ThenBy(p => p.Position, StringComparer.Ordinal)
            .ToList();

        var totalPositionGames = positionSums.Sum(p => p.Games);

        var positions = positionSums
            .Select(p => new ProfilePositionStatReadModel
            {
                Position = p.Position,
                Games = p.Games,
                Rate = RateMath.Rate(p.Games, totalPositionGames),
            })
            .ToList();

        var ranked = snapshot is null
            ? null
            : new ProfileRankedReadModel
            {
                Tier = snapshot.Tier,
                Division = snapshot.Division,
                LeaguePoints = snapshot.LeaguePoints,
                Wins = snapshot.Wins,
                Losses = snapshot.Losses,
                WinRate = RateMath.WinRate(snapshot.Wins, snapshot.Losses),
            };

        logger.LogInformation(
            "{Surface} nameTag={NameTag} account_id={AccountId} mains={MainCount} ranked={Ranked}",
            Surface,
            nameTag,
            account.Id,
            mains.Count,
            ranked is null ? "none" : ranked.Tier);

        return new ProfileReadModel
        {
            Identity = new ProfileIdentityReadModel
            {
                GameName = account.GameName,
                TagLine = account.TagLine,
                PlatformId = account.PlatformId,
                ProfileIconId = account.ProfileIconId,
                SummonerLevel = account.SummonerLevel,
            },
            Ranked = ranked,
            Mains = mains
                .Select(m => new ProfileMainChampionReadModel
                {
                    ChampionId = m.ChampionId,
                    Games = m.ChampionMatches,
                    PlayRate = m.PlayRate,
                    PrimaryPosition = m.PrimaryPosition,
                    IsOtp = m.IsOtp,
                })
                .ToList(),
            Positions = positions,
        };
    }

    private async Task<SnapshotDto?> FetchSnapshotAsync(Guid accountId, CancellationToken ct)
    {
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        return await ctx.RankSnapshots
            .AsNoTracking()
            .Where(s => s.RiotAccountId == accountId)
            .OrderByDescending(s => s.CapturedAtUtc)
            .Select(s => new SnapshotDto(s.Tier, s.Division, s.LeaguePoints, s.Wins, s.Losses))
            .FirstOrDefaultAsync(ct);
    }

    private async Task<List<MainDto>> FetchMainsAsync(string platformId, string puuid, CancellationToken ct)
    {
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        // The repository's helper is keyed on (platformId, puuid) — we
        // intentionally use the same identity here instead of RiotAccountId
        // because that's the natural key the ingestor writes against.
        return await ctx.MainChampionStats
            .AsNoTracking()
            .Where(m => m.PlatformId == platformId && m.Puuid == puuid && m.IsMain)
            .OrderByDescending(m => m.PlayRate)
            .ThenByDescending(m => m.ChampionMatches)
            .Take(MainChampionsCap)
            .Select(m => new MainDto(
                m.ChampionId,
                m.ChampionMatches,
                m.PlayRate,
                m.PrimaryPosition,
                m.IsOtp,
                m.PositionBreakdown))
            .ToListAsync(ct);
    }
}
