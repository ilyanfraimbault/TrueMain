using System.Diagnostics;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public sealed class ChampionSummariesQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IMemoryCache cache,
    ILogger<ChampionSummariesQueryService> logger) : IChampionSummariesQueryService
{
    // The directory list is the same payload for every caller of GET /champions
    // on a given patch and stays valid for the few seconds between ingestor
    // flushes. Caching keyed on the resolved patch means the row-fanning groupby
    // below is paid once per (patch, window) instead of once per request.
    private static readonly TimeSpan SummariesCacheTtl = TimeSpan.FromSeconds(30);

    // Patches change roughly every two weeks, so the resolved "active patch"
    // for an empty query stays stable far longer than the summaries payload.
    // Caching it skips a `SELECT DISTINCT GameVersion` round-trip on every
    // patch-less request — including the ones that hit the summaries cache.
    private static readonly TimeSpan ActivePatchCacheTtl = TimeSpan.FromMinutes(5);
    private const string ActivePatchCacheKey = "champions:summaries:active-patch";

    public async Task<IReadOnlyList<ChampionSummaryReadModel>> GetAllSummariesAsync(string? patch, CancellationToken ct)
    {
        var totalSw = Stopwatch.StartNew();

        var resolveSw = Stopwatch.StartNew();
        var activePatch = await ResolveActivePatchAsync(patch, ct);
        resolveSw.Stop();
        logger.LogInformation(
            "[champions-summaries] resolve_patch requested={RequestedPatch} active={ActivePatch} elapsed={ElapsedMs}ms",
            patch ?? "<null>", activePatch ?? "<null>", resolveSw.ElapsedMilliseconds);

        if (string.IsNullOrEmpty(activePatch))
        {
            totalSw.Stop();
            logger.LogInformation(
                "[champions-summaries] total elapsed={ElapsedMs}ms result=empty",
                totalSw.ElapsedMilliseconds);
            return [];
        }

        return await GetOrComputeSummariesAsync(activePatch, ct);
    }

    private async Task<IReadOnlyList<ChampionSummaryReadModel>> GetOrComputeSummariesAsync(
        string activePatch,
        CancellationToken ct)
    {
        var cacheKey = $"champions:summaries:{activePatch}";
        if (cache.TryGetValue<IReadOnlyList<ChampionSummaryReadModel>>(cacheKey, out var cached) && cached is not null)
        {
            totalSw.Stop();
            logger.LogInformation(
                "[champions-summaries] total elapsed={ElapsedMs}ms result=cache_hit count={Count}",
                totalSw.ElapsedMilliseconds, cached.Count);
            return cached;
        }

        var computeSw = Stopwatch.StartNew();
        var summaries = await ComputeAllSummariesAsync(activePatch, ct);
        computeSw.Stop();
        cache.Set(cacheKey, summaries, SummariesCacheTtl);
        totalSw.Stop();
        logger.LogInformation(
            "[champions-summaries] compute elapsed={ComputeMs}ms total={TotalMs}ms result=miss count={Count}",
            computeSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, summaries.Count);
        return summaries;
    }

    private async Task<string?> ResolveActivePatchAsync(string? requestedPatch, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(requestedPatch))
        {
            return requestedPatch;
        }

        if (cache.TryGetValue<string>(ActivePatchCacheKey, out var cachedPatch) && cachedPatch is not null)
        {
            return cachedPatch;
        }

        var sw = Stopwatch.StartNew();
        var distinctPatches = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == options.Value.QueueId)
            .Select(scope => scope.GameVersion)
            .Distinct()
            .ToListAsync(ct);
        sw.Stop();
        logger.LogInformation(
            "[champions-summaries] sql=distinct_patches rows={Rows} elapsed={ElapsedMs}ms",
            distinctPatches.Count, sw.ElapsedMilliseconds);

        var resolved = ChampionAggregateScopeResolver.ResolvePatchVersion(distinctPatches, requestedPatch: null);
        if (!string.IsNullOrEmpty(resolved))
        {
            cache.Set(ActivePatchCacheKey, resolved, ActivePatchCacheTtl);
        }
        return resolved;
    }

    private async Task<IReadOnlyList<ChampionSummaryReadModel>> ComputeAllSummariesAsync(string activePatch, CancellationToken ct)
    {
        var rowsSw = Stopwatch.StartNew();
        var rows = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == options.Value.QueueId)
            .Where(scope => scope.GameVersion == activePatch)
            .Select(scope => new ChampionSummaryRow(
                scope.ChampionId,
                scope.GameVersion,
                scope.Position,
                scope.Games,
                scope.Wins,
                scope.RiotAccountId,
                scope.AggregatedAtUtc))
            .ToListAsync(ct);
        rowsSw.Stop();
        logger.LogInformation(
            "[champions-summaries] sql=scope_rows rows={Rows} elapsed={ElapsedMs}ms",
            rows.Count, rowsSw.ElapsedMilliseconds);

        var scoped = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Position))
            .ToList();

        if (scoped.Count == 0)
        {
            return [];
        }

        var topBuildsSw = Stopwatch.StartNew();
        var topBuilds = await LoadTopBuildsAsync(activePatch, ct);
        topBuildsSw.Stop();
        logger.LogInformation(
            "[champions-summaries] load_top_builds buckets={Buckets} elapsed={ElapsedMs}ms",
            topBuilds.Count, topBuildsSw.ElapsedMilliseconds);

        // Denominators come from the already-loaded scope rows: lane totals
        // for PickRate and champion totals for LanePlayRate. PickRate is the
        // share of TrueMain games at this lane that picked this champion —
        // a main-population signal, not a meta-wide one (the meta-wide ratio
        // would need a full match_participants scan, which doesn't scale).
        // Sum as long up-front: lane totals fan in over every scoped row on
        // the patch, so this is the widest accumulator in the method — the
        // only one with any plausible long-term risk of int overflow.
        var laneTotals = scoped
            .GroupBy(row => row.Position, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Sum(row => (long)row.Games), StringComparer.Ordinal);
        var championTotals = scoped
            .GroupBy(row => row.ChampionId)
            .ToDictionary(group => group.Key, group => group.Sum(row => row.Games));

        return scoped
            .GroupBy(row => (row.ChampionId, row.Position))
            .Select(group =>
            {
                var games = group.Sum(row => row.Games);
                var wins = group.Sum(row => row.Wins);
                var championTotal = championTotals.GetValueOrDefault(group.Key.ChampionId);
                var laneTotal = laneTotals.GetValueOrDefault(group.Key.Position, 0L);

                topBuilds.TryGetValue((group.Key.ChampionId, group.Key.Position), out var topBuild);
                return new ChampionSummaryReadModel
                {
                    ChampionId = group.Key.ChampionId,
                    Games = games,
                    Wins = wins,
                    WinRate = games == 0 ? 0 : (double)wins / games,
                    PickRate = laneTotal == 0 ? 0 : (double)games / laneTotal,
                    LanePlayRate = championTotal == 0 ? 0 : (double)games / championTotal,
                    TrueMainCount = group.Select(row => row.RiotAccountId).Distinct().Count(),
                    Position = group.Key.Position,
                    PatchVersion = activePatch,
                    LastUpdatedAtUtc = group.Max(row => row.AggregatedAtUtc),
                    TopBuild = topBuild,
                };
            })
            .OrderByDescending(summary => summary.PickRate)
            .ThenBy(summary => summary.ChampionId)
            .ThenBy(summary => summary.Position, StringComparer.Ordinal)
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

    /// <summary>
    /// Resolves the dominant <c>(firstItem, primaryKeystone)</c> bucket for
    /// every <c>(champion, position)</c> pair on
    /// <paramref name="activePatch"/>, then computes the consensus item
    /// path for that bucket via <see cref="ChampionBuildPathAnalyzer"/> —
    /// the same tree-walk used to build the "core" path on the champion
    /// detail page, so the path shown on each list row matches the path on
    /// that champion's detail page for the same slice.
    /// </summary>
    private async Task<IReadOnlyDictionary<(int ChampionId, string Position), TopBuildReadModel>> LoadTopBuildsAsync(
        string activePatch,
        CancellationToken ct)
    {
        var queueId = options.Value.QueueId;

        var groupedSw = Stopwatch.StartNew();
        var grouped = await db.ChampionAggregatePatterns
            .AsNoTracking()
            .Join(
                db.ChampionAggregateScopes.AsNoTracking()
                    .Where(scope => scope.QueueId == queueId && scope.GameVersion == activePatch),
                pattern => pattern.ScopeId,
                scope => scope.Id,
                (pattern, scope) => new
                {
                    scope.ChampionId,
                    scope.Position,
                    pattern.BuildId,
                    pattern.RunePageId,
                    pattern.Games,
                    pattern.Wins,
                })
            .Where(row => row.Position != string.Empty)
            .GroupBy(row => new { row.ChampionId, row.Position, row.BuildId, row.RunePageId })
            .Select(group => new
            {
                group.Key.ChampionId,
                group.Key.Position,
                group.Key.BuildId,
                group.Key.RunePageId,
                Games = group.Sum(row => row.Games),
                Wins = group.Sum(row => row.Wins),
            })
            .ToListAsync(ct);
        groupedSw.Stop();
        logger.LogInformation(
            "[champions-summaries] sql=patterns_join_grouped buckets={Buckets} elapsed={ElapsedMs}ms",
            grouped.Count, groupedSw.ElapsedMilliseconds);

        if (grouped.Count == 0)
        {
            return new Dictionary<(int, string), TopBuildReadModel>();
        }

        var buildIds = grouped.Select(row => row.BuildId).Distinct().ToList();
        var runeIds = grouped.Select(row => row.RunePageId).Distinct().ToList();

        var dimBuildsSw = Stopwatch.StartNew();
        var dimBuilds = await db.ChampionDimBuilds.AsNoTracking()
            .Where(dim => buildIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, ct);
        dimBuildsSw.Stop();
        logger.LogInformation(
            "[champions-summaries] sql=dim_builds rows={Rows} elapsed={ElapsedMs}ms",
            dimBuilds.Count, dimBuildsSw.ElapsedMilliseconds);

        var dimRunesSw = Stopwatch.StartNew();
        var dimRunes = await db.ChampionDimRunePages.AsNoTracking()
            .Where(dim => runeIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, ct);
        dimRunesSw.Stop();
        logger.LogInformation(
            "[champions-summaries] sql=dim_rune_pages rows={Rows} elapsed={ElapsedMs}ms",
            dimRunes.Count, dimRunesSw.ElapsedMilliseconds);

        var result = new Dictionary<(int ChampionId, string Position), TopBuildReadModel>();

        foreach (var laneGroup in grouped.GroupBy(row => (row.ChampionId, row.Position)))
        {
            // Hydrate the bucket rows with their dim entries. Skip any row
            // whose dim lookup is missing (transient state during ingest) or
            // whose build / rune is malformed.
            var enriched = laneGroup
                .Select(row => new
                {
                    row.Games,
                    row.Wins,
                    Build = dimBuilds.GetValueOrDefault(row.BuildId),
                    Rune = dimRunes.GetValueOrDefault(row.RunePageId),
                })
                .Where(row => row.Build is not null && row.Rune is not null
                    && row.Build.BuildItem0 > 0 && row.Rune.PrimaryKeystoneId > 0)
                .ToList();

            if (enriched.Count == 0)
            {
                continue;
            }

            // Same first-tie ordering as ChampionBuildsQueryService: games
            // desc, firstItemId asc, keystoneId asc — so a champion's list
            // row and its detail page land on the same dominant bucket.
            var topBucket = enriched
                .GroupBy(row => (row.Build!.BuildItem0, row.Rune!.PrimaryKeystoneId))
                .Select(group => new
                {
                    FirstItem = group.Key.BuildItem0,
                    Keystone = group.Key.PrimaryKeystoneId,
                    Games = group.Sum(row => row.Games),
                    Wins = group.Sum(row => row.Wins),
                    Rows = group.ToList(),
                })
                .OrderByDescending(bucket => bucket.Games)
                .ThenBy(bucket => bucket.FirstItem)
                .ThenBy(bucket => bucket.Keystone)
                .First();

            // Consensus item path via the same tree-walk + threshold logic
            // the detail page uses for its core build path.
            var sequences = topBucket.Rows
                .Select(row => new ChampionBuildPathAnalyzer.BuildSequence(
                    row.Build!.BuildItem1, row.Build.BuildItem2, row.Build.BuildItem3,
                    row.Build.BuildItem4, row.Build.BuildItem5, row.Build.BuildItem6,
                    row.Games, row.Wins))
                .ToList();
            var tree = ChampionBuildPathAnalyzer.BuildItemTree(sequences, topBucket.Games);
            var (itemPath, _, _) = ChampionBuildPathAnalyzer.WalkPath(
                tree, topBucket.FirstItem, topBucket.Games, topBucket.Wins);

            // Dominant secondary tree within the top bucket.
            var secondaryStyleId = topBucket.Rows
                .GroupBy(row => row.Rune!.SecondaryStyleId)
                .OrderByDescending(group => group.Sum(row => row.Games))
                .ThenBy(group => group.Key)
                .First().Key;

            result[(laneGroup.Key.ChampionId, laneGroup.Key.Position)] = new TopBuildReadModel
            {
                FirstItemId = topBucket.FirstItem,
                PrimaryKeystoneId = topBucket.Keystone,
                SecondaryStyleId = secondaryStyleId,
                ItemPath = itemPath,
            };
        }

        return result;
    }
}
