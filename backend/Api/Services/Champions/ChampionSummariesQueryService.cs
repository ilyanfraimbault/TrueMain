using System.Diagnostics;
using Core.Lol.Ranking;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public sealed class ChampionSummariesQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IOptions<ChampionsListOptions> championsOptions,
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

    // Every cache entry must carry a Size because the shared MemoryCache runs
    // with a SizeLimit (see Program.cs). Without a Size the Set is silently
    // dropped and the value never caches. Count-based: one entry = one unit.
    private static MemoryCacheEntryOptions CacheEntry(TimeSpan ttl) => new()
    {
        AbsoluteExpirationRelativeToNow = ttl,
        Size = 1,
    };

    public async Task<IReadOnlyList<ChampionSummaryReadModel>> GetAllSummariesAsync(
        string? patch, string? eloBracket, CancellationToken ct)
    {
        var totalSw = Stopwatch.StartNew();

        // Resolve the filter to its per-tier bands: cumulative "X+" expands, an
        // exact tier selects only itself. Null → ALL: no elo clause, full union.
        var normalizedBracket = EloBracket.Normalize(eloBracket);
        var bracketBands = EloBracket.ResolveFilter(normalizedBracket);
        var bracketKey = bracketBands is null ? EloBracket.All : normalizedBracket!;

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

        return await GetOrComputeSummariesAsync(activePatch, bracketKey, bracketBands, totalSw, ct);
    }

    private async Task<IReadOnlyList<ChampionSummaryReadModel>> GetOrComputeSummariesAsync(
        string activePatch,
        string bracketKey,
        IReadOnlyList<string>? bracketBands,
        Stopwatch totalSw,
        CancellationToken ct)
    {
        var cacheKey = $"champions:summaries:{activePatch}:{bracketKey}";
        if (cache.TryGetValue<IReadOnlyList<ChampionSummaryReadModel>>(cacheKey, out var cached) && cached is not null)
        {
            totalSw.Stop();
            logger.LogInformation(
                "[champions-summaries] total elapsed={ElapsedMs}ms result=cache_hit count={Count}",
                totalSw.ElapsedMilliseconds, cached.Count);
            return cached;
        }

        var computeSw = Stopwatch.StartNew();
        var summaries = await ComputeAllSummariesAsync(activePatch, bracketBands, ct);
        computeSw.Stop();
        cache.Set(cacheKey, summaries, CacheEntry(SummariesCacheTtl));
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
            .Where(scope => scope.QueueId == (int)options.Value.QueueId)
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
            cache.Set(ActivePatchCacheKey, resolved, CacheEntry(ActivePatchCacheTtl));
        }
        return resolved;
    }

    private async Task<IReadOnlyList<ChampionSummaryReadModel>> ComputeAllSummariesAsync(
        string activePatch, IReadOnlyList<string>? bracketBands, CancellationToken ct)
    {
        // Aggregate per (champion, position) in SQL: a single GROUP BY with
        // SUM(games)/SUM(wins), MAX(aggregated_at) and COUNT(DISTINCT
        // riot_account_id) for the main population. Only the aggregated rows
        // (one per champion/lane, a few hundred at most) cross the wire,
        // instead of one row per (account, champion, lane) slice. The blank
        // Position filter runs server-side too: empty string is the
        // "no position" sentinel (Position is non-nullable, defaults to "").
        // Trim() != "" preserves the previous IsNullOrWhiteSpace semantics
        // and translates to Postgres btrim(...) <> '' under Npgsql.
        var groupsSw = Stopwatch.StartNew();
        var groupsQuery = db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == (int)options.Value.QueueId)
            .Where(scope => scope.GameVersion == activePatch)
            .Where(scope => scope.Position.Trim() != string.Empty);

        // Cumulative elo filter: a null / empty band set is ALL (no clause, the
        // full union incl. Unranked); a non-empty set restricts to the bands at
        // or above the requested threshold (`elo_bracket = ANY(@bands)`).
        if (bracketBands is { Count: > 0 })
        {
            groupsQuery = groupsQuery.Where(scope => bracketBands.Contains(scope.EloBracket));
        }

        var groups = await groupsQuery
            .GroupBy(scope => new { scope.ChampionId, scope.Position })
            .Select(group => new ChampionSummaryGroup(
                group.Key.ChampionId,
                group.Key.Position,
                group.Sum(scope => scope.Games),
                group.Sum(scope => scope.Wins),
                group.Select(scope => scope.RiotAccountId).Distinct().Count(),
                group.Max(scope => scope.AggregatedAtUtc)))
            .ToListAsync(ct);
        groupsSw.Stop();
        logger.LogInformation(
            "[champions-summaries] sql=scope_groups groups={Groups} elapsed={ElapsedMs}ms",
            groups.Count, groupsSw.ElapsedMilliseconds);

        if (groups.Count == 0)
        {
            return [];
        }

        var topBuildsSw = Stopwatch.StartNew();
        var topBuilds = await LoadTopBuildsAsync(activePatch, bracketBands, ct);
        topBuildsSw.Stop();
        logger.LogInformation(
            "[champions-summaries] load_top_builds buckets={Buckets} elapsed={ElapsedMs}ms",
            topBuilds.Count, topBuildsSw.ElapsedMilliseconds);

        // Denominators are derived from the already-aggregated groups: lane
        // totals for PickRate and champion totals for LanePlayRate. Each group
        // already carries its per-(champion,lane) games sum, so re-summing the
        // groups by lane / by champion is exactly equivalent to summing the
        // raw scope rows — but over a handful of rows. PickRate is the share of
        // TrueMain games at this lane that picked this champion — a
        // main-population signal, not a meta-wide one (the meta-wide ratio
        // would need a full match_participants scan, which doesn't scale).
        // Sum lane totals as long: they fan in over every group on the patch,
        // the widest accumulator with any plausible long-term int-overflow risk.
        var laneTotals = groups
            .GroupBy(group => group.Position, StringComparer.Ordinal)
            .ToDictionary(lane => lane.Key, lane => lane.Sum(group => (long)group.Games), StringComparer.Ordinal);
        var championTotals = groups
            .GroupBy(group => group.ChampionId)
            .ToDictionary(champion => champion.Key, champion => champion.Sum(group => group.Games));

        var summaries = groups
            .Select(group =>
            {
                var championTotal = championTotals.GetValueOrDefault(group.ChampionId);
                var laneTotal = laneTotals.GetValueOrDefault(group.Position, 0L);

                topBuilds.TryGetValue((group.ChampionId, group.Position), out var topBuild);
                return new ChampionSummaryReadModel
                {
                    ChampionId = group.ChampionId,
                    Games = group.Games,
                    Wins = group.Wins,
                    WinRate = group.Games == 0 ? 0 : (double)group.Wins / group.Games,
                    PickRate = laneTotal == 0 ? 0 : (double)group.Games / laneTotal,
                    LanePlayRate = championTotal == 0 ? 0 : (double)group.Games / championTotal,
                    TrueMainCount = group.TrueMainCount,
                    Position = group.Position,
                    PatchVersion = activePatch,
                    LastUpdatedAtUtc = group.LastUpdatedAtUtc,
                    TopBuild = topBuild,
                };
            })
            // Drop low-sample lines: a (champion, lane) with too few games is
            // statistical noise — keep it out of the list and the tier ranking
            // (otherwise a 1-game 100%-WR off-role pick flukes to the top of the
            // percentile field). Floor is a product knob (ChampionsList options).
            .Where(summary => summary.Games >= championsOptions.Value.MinSampleGames)
            .OrderByDescending(summary => summary.PickRate)
            .ThenBy(summary => summary.ChampionId)
            .ThenBy(summary => summary.Position, StringComparer.Ordinal)
            .ToList();

        // Tier is a patch-relative ranking, so it can only be assigned once the
        // whole patch's rows exist. Compute it in a single pass over the ordered
        // list and stamp each row in place — the list order itself is unchanged.
        return AssignTiers(summaries);
    }

    private static IReadOnlyList<ChampionSummaryReadModel> AssignTiers(List<ChampionSummaryReadModel> summaries)
    {
        var inputs = summaries
            .Select(summary => new ChampionTierCalculator.TierInput(summary.WinRate, summary.PickRate))
            .ToList();
        var tiers = ChampionTierCalculator.Assign(inputs);

        for (var i = 0; i < summaries.Count; i++)
        {
            summaries[i] = summaries[i] with { Tier = tiers[i] };
        }

        // Wrap before returning: this list is cached in the singleton IMemoryCache,
        // so handing back the bare List<T> would let any caster mutate the shared
        // entry for every request inside the TTL.
        return summaries.AsReadOnly();
    }

    private sealed record ChampionSummaryGroup(
        int ChampionId,
        string Position,
        int Games,
        int Wins,
        int TrueMainCount,
        DateTime LastUpdatedAtUtc);

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
        IReadOnlyList<string>? bracketBands,
        CancellationToken ct)
    {
        var queueId = (int)options.Value.QueueId;

        // Mirror the summaries elo filter so the row's shown build matches the
        // slice its WR / PR are computed from (null / empty = ALL, no clause).
        var scopeQuery = db.ChampionAggregateScopes.AsNoTracking()
            .Where(scope => scope.QueueId == queueId && scope.GameVersion == activePatch);
        if (bracketBands is { Count: > 0 })
        {
            scopeQuery = scopeQuery.Where(scope => bracketBands.Contains(scope.EloBracket));
        }

        var groupedSw = Stopwatch.StartNew();
        var grouped = await db.ChampionAggregatePatterns
            .AsNoTracking()
            .Join(
                scopeQuery,
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
