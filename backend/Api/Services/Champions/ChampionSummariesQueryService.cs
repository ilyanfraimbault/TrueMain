using System.Diagnostics;
using Core.Lol.Ranking;
using Core.Options;
using Data;
using Data.Aggregation;
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
    IOptions<ChampionTierOptions> tierOptions,
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
    private const string PatchListCacheKey = "champions:summaries:patch-list";
    private const string Surface = "champions-summaries";

    // The lifetime games total is one SUM over the whole table — no index leads with
    // QueueId, and the table never shrinks (prod keeps every patch), so this scan only
    // gets longer with the site's age. Cached far longer than the directory because the
    // homepage rounds the figure to three significant digits: at production's rate the
    // *displayed* number moves about twice an hour, so anything finer buys precision
    // the chip throws away.
    private static readonly TimeSpan TotalGamesCacheTtl = TimeSpan.FromMinutes(30);
    private const string TotalGamesCacheKey = "champions:summaries:total-games";

    // How far back the servable walk looks before giving up and serving the newest
    // patch anyway. Bounded because the scan behind it costs one grouped pass per
    // patch and the table only grows: if the four newest patches are all too thin to
    // rank, the site has an ingestion problem that serving a fifth would only hide.
    private const int MaxServableWalkBack = 4;

    public async Task<ChampionSummariesResult> GetAllSummariesAsync(
        string? patch, string? eloBracket, bool truemainsOnly, CancellationToken ct)
    {
        var totalSw = Stopwatch.StartNew();

        // Resolve the filter to its per-tier bands: cumulative "X+" expands, an
        // exact tier selects only itself. Null → ALL: no elo clause, full union.
        //
        // Resolved from the raw value, not from the normalised one: Normalize maps a
        // blank filter and an unrecognised one both to null, so resolving after it
        // would hand every typo the whole population under a rank label (#1224).
        var normalizedBracket = EloBracket.Normalize(eloBracket);
        var bracketBands = EloBracket.ResolveFilterOrEmpty(eloBracket);
        var bracketKey = bracketBands switch
        {
            null => EloBracket.All,
            { Count: 0 } => EloBracket.InvalidToken,
            _ => normalizedBracket!
        };

        var resolveSw = Stopwatch.StartNew();
        var activePatch = await ResolveActivePatchAsync(patch, ct);
        resolveSw.Stop();
        logger.LogInformation(
            "{Surface} resolve_patch requested={RequestedPatch} active={ActivePatch} elapsed={ElapsedMs}ms",
            Surface, patch ?? "<null>", activePatch ?? "<null>", resolveSw.ElapsedMilliseconds);

        if (string.IsNullOrEmpty(activePatch))
        {
            totalSw.Stop();
            logger.LogInformation(
                "{Surface} total elapsed={ElapsedMs}ms result=empty",
                Surface, totalSw.ElapsedMilliseconds);
            return new ChampionSummariesResult();
        }

        return await GetOrComputeSummariesAsync(
            activePatch, bracketKey, bracketBands, truemainsOnly, totalSw, ct);
    }

    private async Task<ChampionSummariesResult> GetOrComputeSummariesAsync(
        string activePatch,
        string bracketKey,
        IReadOnlyList<string>? bracketBands,
        bool truemainsOnly,
        Stopwatch totalSw,
        CancellationToken ct)
    {
        // The population is part of the key: the two answers describe different
        // sets of games, and keying only on (patch, bracket) would serve one
        // under the other's filter.
        var populationKey = truemainsOnly ? "truemains" : "everyone";
        var cacheKey = $"champions:summaries:{activePatch}:{bracketKey}:{populationKey}";
        if (cache.TryGetValue<ChampionSummariesResult>(cacheKey, out var cached) && cached is not null)
        {
            totalSw.Stop();
            logger.LogInformation(
                "{Surface} total elapsed={ElapsedMs}ms result=cache_hit count={Count}",
                Surface, totalSw.ElapsedMilliseconds, cached.Summaries.Count);
            return cached;
        }

        var computeSw = Stopwatch.StartNew();
        var result = await ComputeAllSummariesAsync(activePatch, bracketBands, truemainsOnly, ct);
        computeSw.Stop();
        cache.Set(cacheKey, result, ApiCache.Entry(SummariesCacheTtl));
        totalSw.Stop();
        logger.LogInformation(
            "{Surface} compute elapsed={ComputeMs}ms total={TotalMs}ms result=miss count={Count} totalGames={TotalGames}",
            Surface, computeSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, result.Summaries.Count, result.TotalGames);
        return result;
    }

    public async Task<long> GetTotalGamesAsync(CancellationToken ct)
    {
        if (cache.TryGetValue<long>(TotalGamesCacheKey, out var cached))
        {
            return cached;
        }

        // No patch clause at all — that is the point of the figure. Nullable inside the
        // Sum so an empty table comes back as SQL NULL and maps to 0 instead of failing
        // to materialise into a non-nullable long.
        var sw = Stopwatch.StartNew();
        var total = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == (int)options.Value.QueueId)
            // Mains only (#1346): the homepage chip counts main games analysed,
            // and it is a headline number — it must not quadruple overnight
            // because the aggregate started holding a second population.
            .Where(scope => scope.IsMain)
            .SumAsync(scope => (long?)scope.Games, ct) ?? 0L;
        sw.Stop();
        logger.LogInformation(
            "{Surface} sql=total_games total={Total} elapsed={ElapsedMs}ms",
            Surface, total, sw.ElapsedMilliseconds);

        cache.Set(TotalGamesCacheKey, total, ApiCache.Entry(TotalGamesCacheTtl));
        return total;
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

        var ordered = await LoadPatchesNewestFirstAsync(ct);
        var resolved = await ResolveServablePatchAsync(ordered, ct);
        if (!string.IsNullOrEmpty(resolved))
        {
            cache.Set(ActivePatchCacheKey, resolved, ApiCache.Entry(ActivePatchCacheTtl));
        }
        return resolved;
    }

    /// <summary>
    /// The newest patch that can actually fill a directory (#1109). Measures the
    /// candidates' lines past the floor and hands them to
    /// <see cref="ChampionAggregateScopeResolver.ResolveServablePatch"/>, which walks
    /// back from the newest until one clears
    /// <c>ChampionsList:MinServablePatchLines</c>.
    /// </summary>
    private async Task<string?> ResolveServablePatchAsync(
        IReadOnlyList<string> patchesNewestFirst, CancellationToken ct)
    {
        var minLines = championsOptions.Value.MinServablePatchLines;

        // Nothing to walk back to, or the bar is switched off: keep the pre-#1109
        // path exactly, including its lack of a second query.
        if (minLines <= 0 || patchesNewestFirst.Count <= 1)
        {
            return patchesNewestFirst.FirstOrDefault();
        }

        IReadOnlyList<string> candidates = [.. patchesNewestFirst.Take(MaxServableWalkBack)];
        var linesPastFloor = await LoadLinesPastFloorAsync(candidates, ct);

        var resolved = ChampionAggregateScopeResolver.ResolveServablePatch(candidates, linesPastFloor, minLines);

        // Patch day is the one time this line matters, and it is the one time someone
        // is looking: it says which patch the site is on, which one it declined, and
        // by how much — the three facts the "why is the tier list empty" question
        // needs. Information, not Warning: the fallback is the design working.
        if (!string.Equals(resolved, candidates.FirstOrDefault(), StringComparison.Ordinal))
        {
            logger.LogInformation(
                "{Surface} servable_patch resolved={Resolved} newest={Newest} newestLines={NewestLines} bar={Bar}",
                Surface,
                resolved,
                candidates.FirstOrDefault(),
                linesPastFloor.GetValueOrDefault(candidates.FirstOrDefault() ?? string.Empty),
                minLines);
        }

        return resolved;
    }

    /// <summary>
    /// Every patch the aggregate table holds for the queue, newest first. Cached on
    /// the active-patch TTL: the set only changes when a patch's first fold lands.
    /// </summary>
    private async Task<IReadOnlyList<string>> LoadPatchesNewestFirstAsync(CancellationToken ct)
    {
        if (cache.TryGetValue<IReadOnlyList<string>>(PatchListCacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var sw = Stopwatch.StartNew();
        var distinctPatches = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == (int)options.Value.QueueId)
            // Mains only, and deliberately not parameterised on the population —
            // for the same reason this resolution carries no elo clause: which
            // patch the site serves must not move when the reader changes a
            // filter. Pinning it to the default population also keeps the
            // servable-patch floor below honest once the non-main rows exist.
            .Where(scope => scope.IsMain)
            .Select(scope => scope.GameVersion)
            .Distinct()
            .ToListAsync(ct);
        sw.Stop();
        logger.LogInformation(
            "{Surface} sql=distinct_patches rows={Rows} elapsed={ElapsedMs}ms",
            Surface, distinctPatches.Count, sw.ElapsedMilliseconds);

        var ordered = ChampionAggregateScopeResolver.OrderNewestFirst(distinctPatches);
        cache.Set(PatchListCacheKey, ordered, ApiCache.Entry(ActivePatchCacheTtl));
        return ordered;
    }

    /// <summary>
    /// One grouped scan over the given patches, folded into the single counter the
    /// servable bar is measured against: how many <c>(champion, lane)</c> lines each
    /// patch has past <c>ChampionsList:MinSampleGames</c> — what the directory would
    /// actually render for it. Read only when the active-patch entry misses, once per
    /// its TTL, so it never lands on the hot path.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, int>> LoadLinesPastFloorAsync(
        IReadOnlyList<string> patches, CancellationToken ct)
    {
        if (patches.Count == 0)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var cacheKey = $"champions:summaries:lines-past-floor:{string.Join('|', patches)}";
        if (cache.TryGetValue<IReadOnlyDictionary<string, int>>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        // The same grouping the directory runs, minus the elo clause the resolution
        // must not have: switching bracket may not move the patch the site serves.
        // The population is pinned to mains for the same reason, and for a second
        // one: this is #1109's anti-thin-patch floor, and a patch that clears it
        // only on non-main volume would be served to the default, mains-only
        // directory with a truemain sample that is still too thin to show.
        var sw = Stopwatch.StartNew();
        var grouped = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == (int)options.Value.QueueId)
            .Where(scope => patches.Contains(scope.GameVersion))
            .Where(scope => scope.IsMain)
            .GroupBy(scope => new { scope.GameVersion, scope.ChampionId, scope.Position })
            // Projected into an anonymous type and mapped after materialisation, the
            // same shape ComputeAllSummariesAsync uses: a grouped projection straight
            // into a struct's constructor is the kind of expression the provider is
            // free to refuse, and it would refuse it at runtime on the homepage.
            .Select(group => new
            {
                group.Key.GameVersion,
                group.Key.ChampionId,
                group.Key.Position,
                Games = group.Sum(scope => scope.Games)
            })
            .ToListAsync(ct);
        sw.Stop();
        logger.LogInformation(
            "{Surface} sql=lines_past_floor patches={Patches} rows={Rows} elapsed={ElapsedMs}ms",
            Surface, patches.Count, grouped.Count, sw.ElapsedMilliseconds);

        var rows = grouped
            .Select(row => new ChampionDirectoryLine(row.GameVersion, row.ChampionId, row.Position, row.Games))
            .ToList();

        var floor = championsOptions.Value.MinSampleGames;

        // Only the rows carrying a lane count — the same split ComputeAllSummariesAsync
        // makes between the patch total and the ranked rows. A patch with rows but no
        // line past the floor is simply absent here, which the resolver reads as zero.
        IReadOnlyDictionary<string, int> linesByPatch = ChampionDirectoryLines.Fold(rows)
            .Where(line => ChampionDirectoryLines.ClearsFloor(line, floor))
            .GroupBy(line => line.Patch, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        cache.Set(cacheKey, linesByPatch, ApiCache.Entry(SummariesCacheTtl));
        return linesByPatch;
    }

    private async Task<ChampionSummariesResult> ComputeAllSummariesAsync(
        string activePatch,
        IReadOnlyList<string>? bracketBands,
        bool truemainsOnly,
        CancellationToken ct)
    {
        // Aggregate per (champion, position) in SQL: a single GROUP BY with
        // SUM(games)/SUM(wins), MAX(aggregated_at) and COUNT(DISTINCT
        // riot_account_id) for the main population. Only the aggregated rows
        // (one per champion/lane, a few hundred at most) cross the wire,
        // instead of one row per (account, champion, lane) slice.
        //
        // No Position filter here (#972): the ranked directory still needs one
        // (a blank Position — the "no position" sentinel, since Position is
        // non-nullable — carries no lane to score), but the homepage's "games
        // analyzed" total needs to sum every group the patch actually has, so
        // that filter moves to memory below, after the total is taken.
        var groupsSw = Stopwatch.StartNew();
        var groupsQuery = db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == (int)options.Value.QueueId)
            .Where(scope => scope.GameVersion == activePatch);

        // Cumulative elo filter: null is ALL (no clause, the full union incl.
        // Unranked); a non-null set restricts to those bands — empty included,
        // which correctly matches nothing rather than widening back to ALL for
        // a rejected filter (see EloBracket.ResolveFilterOrEmpty).
        if (bracketBands is not null)
        {
            groupsQuery = groupsQuery.Where(scope => bracketBands.Contains(scope.EloBracket));
        }

        // Truemains filter (#1346): mains of the champion only, or every tracked
        // player who has games on it.
        if (truemainsOnly)
        {
            groupsQuery = groupsQuery.Where(scope => scope.IsMain);
        }

        var allGroups = await groupsQuery
            .GroupBy(scope => new { scope.ChampionId, scope.Position })
            .Select(group => new ChampionSummaryGroup(
                group.Key.ChampionId,
                group.Key.Position,
                group.Sum(scope => scope.Games),
                group.Sum(scope => scope.Wins),
                // Counts *mains* whatever the population filter is, so the field
                // keeps meaning what its name says: under `truemainsOnly: false`
                // an unqualified distinct count would be "tracked players", which
                // is a different number wearing the truemain label.
                group.Where(scope => scope.IsMain).Select(scope => scope.RiotAccountId).Distinct().Count(),
                group.Max(scope => scope.AggregatedAtUtc)))
            .ToListAsync(ct);
        groupsSw.Stop();
        logger.LogInformation(
            "{Surface} sql=scope_groups groups={Groups} elapsed={ElapsedMs}ms",
            Surface, allGroups.Count, groupsSw.ElapsedMilliseconds);

        // Every champion_aggregate_scopes row that matched the filters above
        // folds into exactly one group here (position-less groups included),
        // so this sum is the true total — see ChampionSummariesResult.TotalGames.
        var totalGames = allGroups.Sum(group => (long)group.Games);

        // Trim() != "" preserves the previous IsNullOrWhiteSpace semantics: a
        // blank Position has no lane to score and is excluded from the ranked
        // rows (but was already counted in totalGames above).
        var groups = allGroups.Where(group => group.Position.Trim() != string.Empty).ToList();

        if (groups.Count == 0)
        {
            return new ChampionSummariesResult { PatchVersion = activePatch, TotalGames = totalGames };
        }

        var topBuildsSw = Stopwatch.StartNew();
        var topBuilds = await LoadTopBuildsAsync(activePatch, bracketBands, truemainsOnly, ct);
        topBuildsSw.Stop();
        logger.LogInformation(
            "{Surface} load_top_builds buckets={Buckets} elapsed={ElapsedMs}ms",
            Surface, topBuilds.Count, topBuildsSw.ElapsedMilliseconds);

        // Null for every patch older than #920: the scope simply has no rows, and
        // BanRate stays null so the UI shows a gap instead of a fabricated 0%.
        var banScopes = await ChampionBanRateQueries.LoadAsync(db, [activePatch], bracketBands, ct);
        var banScope = banScopes.GetValueOrDefault(activePatch);

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
                    WinRate = RateMath.Rate(group.Wins, group.Games),
                    PickRate = RateMath.Rate(group.Games, laneTotal),
                    LanePlayRate = RateMath.Rate(group.Games, championTotal),
                    TrueMainCount = group.TrueMainCount,
                    BanRate = banScope?.RateFor(group.ChampionId),
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

        // Then keep only each champion's dominant lanes, so the directory is a
        // list of champions rather than of every (champion, lane) pair the
        // population has ever produced (#1082). Before tiering on purpose: the
        // tier is a percentile within a lane, and an off-role line is not one
        // of that lane's peers.
        var dominant = ChampionDominantLaneFilter
            .KeepDominantLanes(summaries, championsOptions.Value)
            .ToList();
        logger.LogInformation(
            "{Surface} dominant_lanes kept={Kept} dropped={Dropped} maxLanes={MaxLanes} minSecondaryShare={MinShare}",
            Surface, dominant.Count, summaries.Count - dominant.Count,
            championsOptions.Value.MaxLanesPerChampion, championsOptions.Value.MinSecondaryLanePlayRate);

        // Tier is a lane-relative ranking (see AssignTiers), so it can only be
        // assigned once the whole patch's rows exist. Compute it in a single
        // pass over the ordered list and stamp each row in place — the list
        // order itself is unchanged.
        var tiered = AssignTiers(dominant, tierOptions.Value);

        return new ChampionSummariesResult
        {
            PatchVersion = activePatch,
            TotalGames = totalGames,
            Summaries = tiered,
        };
    }

    // Evaluate one Position's rows in isolation rather than the whole patch at
    // once: ChampionTierCalculator.Evaluate percentile-ranks pick/ban/win
    // *within* a lane already, but its S/A/B/C/D bucket cutoff is a plain
    // rank/count over whatever set it was given. Mixing every position into
    // one call would let a thin lane (a narrow eloBracket crossed with a
    // less-played position can leave only a handful of rows clearing
    // MinSampleGames) trivially top its own tiny peer group on every metric —
    // reintroducing, via lane population size, the exact "flukes into
    // S-tier" failure this whole rework exists to fix for game count. This is
    // the only place a tier is computed: GET /champions/tierlist reshapes these
    // same stamped rows instead of re-tiering them, so a row's Tier/TierScore
    // cannot differ between the two endpoints for the same (patch, eloBracket).
    private IReadOnlyList<ChampionSummaryReadModel> AssignTiers(
        List<ChampionSummaryReadModel> summaries, ChampionTierOptions options)
    {
        var results = new ChampionTierCalculator.TierResult[summaries.Count];

        foreach (var lane in summaries
                     .Select((summary, index) => (summary, index))
                     .GroupBy(row => row.summary.Position, StringComparer.Ordinal))
        {
            var laneRows = lane.ToList();

            // Ban data is populated per-patch, not per-champion (#920), so
            // every row of a lane is expected to agree on whether BanRate is
            // null. A lane with both null and non-null rows means the ban
            // ingestion partially failed — ChampionTierCalculator degrades
            // safely (drops the ban term for the whole lane, see its "Missing
            // ban data" doc), but that's a silent quality drop worth a log.
            if (laneRows.Select(row => row.summary.BanRate is null).Distinct().Count() > 1)
            {
                logger.LogWarning(
                    "{Surface} lane={Position} has a mix of null and non-null BanRate — ban ingestion likely "
                    + "partially failed for this patch; ChampionTierCalculator drops the ban term for the whole lane",
                    Surface, lane.Key);
            }

            var inputs = laneRows
                .Select(row => new ChampionTierCalculator.TierInput(
                    row.summary.Position, row.summary.Games, row.summary.Wins,
                    row.summary.PickRate, row.summary.BanRate))
                .ToList();
            var laneResults = ChampionTierCalculator.Evaluate(inputs, options);

            for (var i = 0; i < laneRows.Count; i++)
            {
                results[laneRows[i].index] = laneResults[i];
            }
        }

        for (var i = 0; i < summaries.Count; i++)
        {
            summaries[i] = summaries[i] with { Tier = results[i].Tier, TierScore = results[i].Score };
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
        bool truemainsOnly,
        CancellationToken ct)
    {
        var queueId = (int)options.Value.QueueId;

        // Mirror the summaries elo filter so the row's shown build matches the
        // slice its WR / PR are computed from (null = ALL, no clause; a
        // non-null set — empty included — restricts, see ResolveFilterOrEmpty).
        var scopeQuery = db.ChampionAggregateScopes.AsNoTracking()
            .Where(scope => scope.QueueId == queueId && scope.GameVersion == activePatch);
        if (bracketBands is not null)
        {
            scopeQuery = scopeQuery.Where(scope => bracketBands.Contains(scope.EloBracket));
        }

        // Same population as the WR / PR beside it, for the same reason.
        if (truemainsOnly)
        {
            scopeQuery = scopeQuery.Where(scope => scope.IsMain);
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
            "{Surface} sql=patterns_join_grouped buckets={Buckets} elapsed={ElapsedMs}ms",
            Surface, grouped.Count, groupedSw.ElapsedMilliseconds);

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
            "{Surface} sql=dim_builds rows={Rows} elapsed={ElapsedMs}ms",
            Surface, dimBuilds.Count, dimBuildsSw.ElapsedMilliseconds);

        var dimRunesSw = Stopwatch.StartNew();
        var dimRunes = await db.ChampionDimRunePages.AsNoTracking()
            .Where(dim => runeIds.Contains(dim.Id))
            .ToDictionaryAsync(dim => dim.Id, ct);
        dimRunesSw.Stop();
        logger.LogInformation(
            "{Surface} sql=dim_rune_pages rows={Rows} elapsed={ElapsedMs}ms",
            Surface, dimRunes.Count, dimRunesSw.ElapsedMilliseconds);

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
