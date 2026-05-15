using Core.Lol.Patches;
using Core.Options;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public sealed class ChampionFoundationQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IMemoryCache cache) : IChampionFoundationQueryService
{
    // The directory list is the same payload for every caller of GET /champions
    // and stays valid for the few seconds between ingestor flushes. Caching here
    // means the row-fanning groupby below is paid once per window instead of
    // once per request as the table grows on (account, patch, platform, position).
    private const string SummariesCacheKey = "champions:summaries";
    private static readonly TimeSpan SummariesCacheTtl = TimeSpan.FromSeconds(30);

    public Task<ChampionFoundationReadModel?> GetAsync(
        int championId,
        Guid? riotAccountId,
        string? patch,
        string? platformId,
        string? position,
        CancellationToken ct)
        => GetAsync(championId, riotAccountId, patch, platformId, position, ChampionPatternPivot.None, ct);

    public async Task<ChampionFoundationReadModel?> GetAsync(
        int championId,
        Guid? riotAccountId,
        string? patch,
        string? platformId,
        string? position,
        ChampionPatternPivot pivot,
        CancellationToken ct)
    {
        var scopes = await LoadScopedScopesAsync(championId, riotAccountId, patch, platformId, position, ct);
        if (scopes is null)
        {
            return null;
        }

        var scopeIds = scopes.Select(scope => scope.Id).ToList();
        var projection = await ChampionPatternProjector.ProjectAsync(db, scopeIds, pivot, ct);

        // SampleSize must reflect the (possibly filtered) pattern volume so
        // play-rates compute against the right denominator. With no pivot
        // this collapses to the legacy scope-level total; with a build
        // pivot it shrinks to the games that played that build, which is
        // the correct denominator for "given build X, how often does this
        // rune page show up".
        var sampleSize = projection.Builds.Sum(build => build.Games);
        if (sampleSize == 0)
        {
            sampleSize = scopes.Sum(scope => scope.Games);
        }

        var advanced = ChampionOptionProjector.BuildAdvancedDetails(
            projection.StarterItems,
            projection.SpellPairs,
            projection.SkillOrders,
            projection.RunePages,
            sampleSize);

        return new ChampionFoundationReadModel
        {
            Summary = BuildSummary(championId, scopes.First().GameVersion, scopes),
            Core = ChampionCoreBuilder.Build(sampleSize, advanced, projection.Builds),
            Advanced = advanced
        };
    }

    public async Task<IReadOnlyList<ChampionSummaryReadModel>> GetAllSummariesAsync(CancellationToken ct)
    {
        if (cache.TryGetValue<IReadOnlyList<ChampionSummaryReadModel>>(SummariesCacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var summaries = await ComputeAllSummariesAsync(ct);
        cache.Set(SummariesCacheKey, summaries, SummariesCacheTtl);
        return summaries;
    }

    private async Task<IReadOnlyList<ChampionSummaryReadModel>> ComputeAllSummariesAsync(CancellationToken ct)
    {
        // One pass over the active queue. Only the columns BuildSummary needs
        // are projected so the materialised payload stays small even when
        // the table grows wide on (account, patch, platform, position).
        var rows = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == options.Value.QueueId)
            .Select(scope => new ChampionSummaryRow(
                scope.ChampionId,
                scope.GameVersion,
                scope.Position,
                scope.Games,
                scope.Wins,
                scope.RiotAccountId,
                scope.AggregatedAtUtc))
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            return [];
        }

        // For each champion, resolve its own latest patch (champions ship and
        // get reworked at different times — a global "latest patch" would hide
        // anyone who hasn't been touched recently), then aggregate against
        // that patch only. Mirrors what LoadScopedScopesAsync does for one
        // champion, applied per group here.
        return rows
            .GroupBy(row => row.ChampionId)
            .Select(group =>
            {
                var latestPatch = ChampionAggregateScopeResolver.ResolvePatchVersion(
                    group.Select(row => row.GameVersion),
                    requestedPatch: null);
                if (string.IsNullOrEmpty(latestPatch))
                {
                    return null;
                }

                var scoped = group.Where(row => string.Equals(row.GameVersion, latestPatch, StringComparison.Ordinal)).ToList();
                var totalGames = scoped.Sum(row => row.Games);
                var totalWins = scoped.Sum(row => row.Wins);
                var trueMainCount = scoped.Select(row => row.RiotAccountId).Distinct().Count();
                var dominantPosition = ChampionAggregateScopeResolver.ResolveDominantPosition(
                    scoped.Select(row => (row.Position, row.Games)));

                return new ChampionSummaryReadModel
                {
                    ChampionId = group.Key,
                    Games = totalGames,
                    WinRate = ChampionOptionProjector.ComputeRate(totalWins, totalGames),
                    TrueMainCount = trueMainCount,
                    Position = dominantPosition,
                    LatestPatchVersion = latestPatch,
                    LastUpdatedAtUtc = scoped.Max(row => row.AggregatedAtUtc)
                };
            })
            .OfType<ChampionSummaryReadModel>()
            .OrderByDescending(summary => summary.Games)
            .ThenBy(summary => summary.ChampionId)
            .ToList();
    }

    private sealed record ChampionSummaryRow(
        int ChampionId,
        string GameVersion,
        string Position,
        int Games,
        int Wins,
        Guid RiotAccountId,
        DateTime AggregatedAtUtc);

    internal async Task<IReadOnlyList<ChampionAggregateScope>?> LoadScopedScopesAsync(
        int championId,
        Guid? riotAccountId,
        string? patch,
        string? platformId,
        string? position,
        CancellationToken ct)
    {
        // Canonicalise the requested patch up-front so the SQL filter and
        // the materialised rows both use the persisted "major.minor" form
        // (callers — especially non-controller ones — may pass a full Riot
        // version string like "16.4.521.123").
        var normalizedPatch = string.IsNullOrWhiteSpace(patch)
            ? null
            : PatchVersion.Normalize(patch);

        // Pass 1: a light projection over the (champion, queue, account,
        // patch, platform, position) slice. We only need the columns that
        // feed the dominant-patch and dominant-position resolution; pulling
        // entities would also drag the RiotAccount navigation surface for
        // no reason. The strict equality filters (championId, queueId,
        // riotAccountId, platformId, and patch/position when supplied)
        // already live in WhereChampionScope and translate to SQL.
        var baseQuery = db.ChampionAggregateScopes
            .AsNoTracking()
            .WhereChampionScope(championId, options.Value.QueueId, riotAccountId, normalizedPatch, platformId, position);

        var resolutionRows = await baseQuery
            .Select(scope => new ScopeResolutionRow(scope.GameVersion, scope.Position, scope.Games))
            .ToListAsync(ct);
        if (resolutionRows.Count == 0)
        {
            return null;
        }

        var selectedPatch = ChampionAggregateScopeResolver.ResolvePatchVersion(
            resolutionRows.Select(row => row.GameVersion),
            normalizedPatch);
        if (string.IsNullOrWhiteSpace(selectedPatch))
        {
            return null;
        }

        var effectivePosition = string.IsNullOrWhiteSpace(position)
            ? ChampionAggregateScopeResolver.ResolveDominantPosition(
                resolutionRows
                    .Where(row => string.Equals(row.GameVersion, selectedPatch, StringComparison.Ordinal))
                    .Select(row => (row.Position, row.Games)))
            : position;

        // Pass 2: re-query with the resolved patch (and position when
        // available) pushed into SQL via WhereChampionScope. This keeps
        // the materialisation tight even when the caller didn't pin a
        // patch upfront.
        var scopedScopes = await db.ChampionAggregateScopes
            .AsNoTracking()
            .WhereChampionScope(
                championId,
                options.Value.QueueId,
                riotAccountId,
                selectedPatch,
                platformId,
                string.IsNullOrWhiteSpace(effectivePosition) ? null : effectivePosition)
            .ToListAsync(ct);

        return scopedScopes.Count == 0 ? null : scopedScopes;
    }

    private sealed record ScopeResolutionRow(string GameVersion, string Position, int Games);

    private static ChampionSummaryReadModel BuildSummary(
        int championId,
        string latestPatchVersion,
        IReadOnlyCollection<ChampionAggregateScope> scopes)
    {
        var totalGames = scopes.Sum(scope => scope.Games);
        var totalWins = scopes.Sum(scope => scope.Wins);
        var trueMainCount = scopes.Select(scope => scope.RiotAccountId).Distinct().Count();
        var dominantPosition = ChampionAggregateScopeResolver.ResolveDominantPosition(scopes);

        return new ChampionSummaryReadModel
        {
            ChampionId = championId,
            Games = totalGames,
            WinRate = ChampionOptionProjector.ComputeRate(totalWins, totalGames),
            TrueMainCount = trueMainCount,
            Position = dominantPosition,
            LatestPatchVersion = latestPatchVersion,
            LastUpdatedAtUtc = scopes.Max(scope => scope.AggregatedAtUtc)
        };
    }
}
