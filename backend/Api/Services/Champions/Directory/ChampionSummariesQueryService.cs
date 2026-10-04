using System.Diagnostics;
using Core.Lol.Ranking;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Scopes;

namespace TrueMain.Services.Champions.Directory;

public interface IChampionSummariesQueryService
{
    /// <summary>
    /// Lightweight directory query: one <see cref="ChampionSummaryReadModel"/>
    /// per <c>(champion, position)</c> pair on the active queue, all rows
    /// pinned to a single patch (<paramref name="patch"/> if non-null and
    /// canonical, otherwise the global latest patch in the aggregate table),
    /// wrapped with the resolved patch and true total games (#972 — see
    /// <see cref="ChampionSummariesResult"/>). Used by the champions list /
    /// index page and the homepage overview; callers that need builds, runes
    /// or patterns go through <c>GET /champions/{id}</c>.
    ///
    /// <paramref name="eloBracket"/> is a cumulative "X+" threshold (see
    /// <see cref="Core.Lol.Ranking.EloBracket"/>); null / ALL spans every band.
    ///
    /// <paramref name="truemainsOnly"/> selects the population: mains of each
    /// champion (the default, and the only population the aggregate held before
    /// #1346) or every tracked player with games on it.
    /// </summary>
    Task<ChampionSummariesResult> GetAllSummariesAsync(
        string? patch, string? eloBracket, bool truemainsOnly, CancellationToken ct);

    /// <summary>
    /// Every aggregated game on the tracked queue, all patches summed — the homepage's
    /// lifetime volume chip. Includes below-floor and position-less scopes, the same
    /// population <see cref="ChampionSummariesResult.TotalGames"/> counts for one patch.
    ///
    /// <para>
    /// One <c>SUM</c> pushed to SQL rather than a directory per patch: the number is a
    /// scalar, and the patches behind it only ever grow. Cached for half an hour so the
    /// most-hit page on the site does not scan the aggregate table per request.
    /// </para>
    /// </summary>
    Task<long> GetTotalGamesAsync(CancellationToken ct);
}

public sealed class ChampionSummariesQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IOptions<ChampionsListOptions> championsOptions,
    IOptions<ChampionTierOptions> tierOptions,
    IChampionReadCache cache,
    ILogger<ChampionSummariesQueryService> logger) : IChampionSummariesQueryService
{
    // Every entry here and in ChampionDirectoryPatchResolver goes through
    // IChampionReadCache, so each is keyed by the ingestor's aggregation version
    // rather than by a clock (#1368). That is the
    // right lifetime for all five: the directory payload, the resolved active patch,
    // the patch list, the servable-lines counter and the lifetime total are all folds
    // over champion_aggregate_scopes, and none of them can change until the
    // aggregation lane writes again. An answer computed on an empty database is not a
    // problem either — the version token moves the moment the first fold lands, so the
    // empty entry is simply never consulted again.
    internal const string Surface = "champions-summaries";

    // The lifetime games total is one SUM over the whole table — no index leads with
    // QueueId, and the table never shrinks (prod keeps every patch), so this scan only
    // gets longer with the site's age. It used to buy its own 30-minute TTL on the
    // argument that the homepage rounds the figure to three significant digits anyway;
    // keyed by aggregation version it is simply exact until the number actually moves.
    private const string TotalGamesCacheKey = "champions:summaries:total-games";

    private readonly ChampionDirectoryPatchResolver patchResolver =
        new(db, options, championsOptions, cache, logger);

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
        var activePatch = await patchResolver.ResolveActivePatchAsync(patch, ct);
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

        var computeSw = Stopwatch.StartNew();
        var result = await cache.GetOrComputeAsync(
            cacheKey,
            token => ComputeAllSummariesAsync(activePatch, bracketBands, truemainsOnly, token),
            ct);
        computeSw.Stop();
        totalSw.Stop();
        logger.LogInformation(
            "{Surface} compute elapsed={ComputeMs}ms total={TotalMs}ms count={Count} totalGames={TotalGames}",
            Surface, computeSw.ElapsedMilliseconds, totalSw.ElapsedMilliseconds, result.Summaries.Count, result.TotalGames);
        return result;
    }

    public Task<long> GetTotalGamesAsync(CancellationToken ct)
        => cache.GetOrComputeAsync(TotalGamesCacheKey, ComputeTotalGamesAsync, ct);

    private async Task<long> ComputeTotalGamesAsync(CancellationToken ct)
    {
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

        return total;
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
        var topBuilds = await ChampionDirectoryTopBuilds.LoadAsync(
            db, (int)options.Value.QueueId, activePatch, bracketBands, truemainsOnly, logger, ct);
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
        var tiered = ChampionDirectoryTiering.AssignTiers(dominant, tierOptions.Value, logger);

        return new ChampionSummariesResult
        {
            PatchVersion = activePatch,
            TotalGames = totalGames,
            Summaries = tiered,
        };
    }

    private sealed record ChampionSummaryGroup(
        int ChampionId,
        string Position,
        int Games,
        int Wins,
        int TrueMainCount,
        DateTime LastUpdatedAtUtc);
}
