using System.Diagnostics;
using Core.Lol.Map;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Truemains;
using TrueMain.Services.Truemains.Identity;

namespace TrueMain.Services.Truemains.Leaderboard;

/// <summary>
/// The column the leaderboard is ranked on.
/// </summary>
public enum LeaderboardSort
{
    /// <summary>Current ranked standing (the materialised <c>riot_accounts."Score"</c>). The default.</summary>
    Rank = 0,

    /// <summary>TrueMain's dedication score for each row's signature champion, computed at read time.</summary>
    Dedication = 1,

    /// <summary>Ranked games on the player's main champions (the row's Games cell).</summary>
    Games = 2,

    /// <summary>KDA on the player's main champions (the row's KDA cell).</summary>
    Kda = 3,

    /// <summary>Ranked win rate of the latest rank snapshot (the row's WR cell).</summary>
    WinRate = 4,
}

public interface ITruemainsLeaderboardQueryService
{
    /// <summary>
    /// Returns a paged slice of the truemains leaderboard. The list is
    /// restricted to accounts on the regions the leaderboard exposes
    /// (<c>europe</c> / <c>americas</c> / <c>korea</c>) and to those that
    /// have at least one rank snapshot — unranked accounts are out of scope
    /// for V1. Optional filters narrow by region, dominant role of a main
    /// champion, a specific champion id, and/or (<paramref name="otpOnly"/>)
    /// one-trick-pony status on a main champion. <paramref name="sort"/> picks
    /// the ranking column.
    /// </summary>
    Task<LeaderboardResponse> GetAsync(
        int page,
        int pageSize,
        string? region,
        string? position,
        int? championId,
        bool otpOnly,
        LeaderboardSort sort,
        CancellationToken ct);
}

/// <summary>
/// Orchestrates one leaderboard request: response cache and coalescing, the
/// ranking phase (<see cref="LeaderboardRanking"/>, over the eligibility SQL in
/// <see cref="LeaderboardEligibility"/>), then the concurrent hydration of the
/// page slice (<see cref="LeaderboardPageHydration"/> and the shared
/// per-row reads) and the per-phase timing log.
/// </summary>
public sealed class TruemainsLeaderboardQueryService(
    TrueMainDbContext db,
    IDbContextFactory<TrueMainDbContext> dbFactory,
    IOptions<TruemainsLeaderboardOptions> options,
    IOptions<MainAnalysisOptions> mainAnalysisOptions,
    IMemoryCache cache,
    ILogger<TruemainsLeaderboardQueryService> logger) : ITruemainsLeaderboardQueryService
{
    internal const string Surface = "truemain-leaderboard";

    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 50;

    // The leaderboard is dominated by page-1 traffic with no filters, and the
    // four SQL queries that make a fresh response (Count + FetchPage +
    // FetchTopChampions + FetchStats) cost more than the response itself is
    // worth re-deriving every second. The numbers underneath only shift when
    // the ingestor flushes a new batch of rank snapshots or matches — that
    // happens on a multi-minute cadence, so a 30s TTL trades a few seconds of
    // staleness for a massive drop in DB load. Mirrors the TTL
    // ChampionSummariesQueryService uses for the same reason.
    private static readonly TimeSpan ResponseCacheTtl = TimeSpan.FromSeconds(30);

    // Static because the service is scoped: the whole point is to coalesce across
    // concurrent *requests*, which each get their own instance.
    private static readonly RequestCoalescer<LeaderboardResponse> ResponseCoalescer = new();

    // Ranked solo queue. Matches the queue used by MainStatsCalculator
    // for main_champion_stats, so the "games" / KDA / winrate cell stays
    // consistent with the player's top-champions cell on the same row.
    internal const int RankedQueueId = (int)LolQueueId.RankedSoloDuo;

    private readonly LeaderboardRanking ranking = new(db, dbFactory, mainAnalysisOptions, cache, logger);

    public async Task<LeaderboardResponse> GetAsync(
        int page,
        int pageSize,
        string? region,
        string? position,
        int? championId,
        bool otpOnly,
        LeaderboardSort sort,
        CancellationToken ct)
    {
        var totalSw = Stopwatch.StartNew();

        var normalizedPosition = LanePositions.Normalize(position);
        var championFilter = championId is > 0 ? championId : null;
        var clampedPageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);
        var clampedPage = Math.Max(page, 1);
        var offset = (clampedPage - 1) * clampedPageSize;

        // Region narrowing: an explicit ?region= picks one of the three
        // exposed pills, anything else falls back to "every platform the
        // leaderboard surfaces" so the count and the page slice agree on
        // which accounts are eligible.
        var platforms = (RegionFilterParser.Parse(region)
                         ?? RegionFilterParser.AllExposedPlatforms())
                        .ToArray();

        if (platforms.Length == 0)
        {
            return Empty(clampedPage, clampedPageSize);
        }

        var minGames = Math.Max(0, options.Value.MinRankedGames);
        var filter = new LeaderboardFilter(platforms, championFilter, normalizedPosition, minGames, otpOnly);

        // Page-1-no-filter is the dominant shape of /truemains traffic; the
        // four SQL queries that compose a fresh response cost far more than
        // the JSON is worth re-deriving every second. Cache the assembled
        // response keyed on the request shape — the snapshot rate underneath
        // moves on a multi-minute cadence (RankSnapshotIngestion + match
        // ingest), so the TTL trades a few seconds of staleness for a large
        // drop in DB load. Mirrors ChampionSummariesQueryService's TTL.
        var cacheKey = $"truemains:leaderboard:{filter.Key}:{sort}:{clampedPage}:{clampedPageSize}";
        if (cache.TryGetValue<LeaderboardResponse>(cacheKey, out var cached) && cached is not null)
        {
            totalSw.Stop();
            logger.LogInformation(
                "{Surface} page={Page} pageSize={PageSize} region={Region} position={Position} championId={ChampionId} minGames={MinGames} otpOnly={OtpOnly} sort={Sort} rows={Rows} total={Total} elapsed={ElapsedMs}ms result=cache_hit",
                Surface, clampedPage, clampedPageSize, region ?? "all", normalizedPosition ?? "any", championFilter, minGames, otpOnly, sort,
                cached.Rows.Count, cached.Total, totalSw.ElapsedMilliseconds);
            return cached;
        }

        // Miss. Every request that missed the same page and filters together shares
        // one computation instead of each running the count, the page and six
        // hydration queries (#1570): a champion page asks for its mains card on
        // every view, and under load those misses arrived together. The owner
        // waits for the pass even if its visitor leaves, since Count and Page run
        // on its request-scoped context (see RequestCoalescer).
        var shape = new ResponseShape(cacheKey, filter, sort, clampedPage, clampedPageSize, offset, region);
        return await ResponseCoalescer.GetOrJoinAsync(cacheKey, () => ComputeResponseAsync(shape), ct, ownerAwaitsToCompletion: true);
    }

    private async Task<LeaderboardResponse> ComputeResponseAsync(ResponseShape shape)
    {
        var (cacheKey, filter, sort, clampedPage, clampedPageSize, offset, region) = shape;
        var (platforms, championFilter, normalizedPosition, minGames, otpOnly) = filter;
        // Detached from any single caller's token: the pass is shared.
        var ct = CancellationToken.None;
        var totalSw = Stopwatch.StartNew();

        // Re-check under the coalescer: a pass for this key may have finished and
        // cached its response while this caller was starting.
        if (cache.TryGetValue<LeaderboardResponse>(cacheKey, out var justCached) && justCached is not null)
        {
            return justCached;
        }

        // Ranking by dedication or by a stat can't seek an index — the figure
        // is derived at read time from other tables — so those paths read the
        // whole eligible population once and slice the page from it. The
        // default rank ordering keeps the cheap Count + indexed OFFSET on
        // riot_accounts."Score".
        var (rankingResult, countMs) = await TimedAsync(() => sort switch
        {
            LeaderboardSort.Dedication => ranking.RankByDedicationAsync(filter, ResponseCacheTtl, ct),
            _ when LeaderboardStatLines.Serves(sort) => ranking.RankByStatAsync(filter, sort, RankedQueueId, ResponseCacheTtl, ct),
            _ => ranking.CountByRankAsync(filter, ct),
        });

        var total = rankingResult.Total;
        if (total == 0)
        {
            var empty = Empty(clampedPage, clampedPageSize);
            // Cache the empty response — a filter that yields nothing still
            // pays for the Count SQL on every visit, and those are the same
            // requests an attacker / overzealous client would replay.
            cache.Set(cacheKey, empty, ApiCache.Entry(ResponseCacheTtl));
            // An empty result is exactly when countMs is worth seeing: an
            // over-restrictive filter or a misconfigured MinRankedGames is
            // diagnosed here, not on the populated path.
            totalSw.Stop();
            logger.LogInformation(
                "{Surface} page={Page} pageSize={PageSize} region={Region} position={Position} championId={ChampionId} minGames={MinGames} otpOnly={OtpOnly} sort={Sort} rows=0 total=0 countMs={CountMs:F1} elapsed={ElapsedMs}ms result=empty",
                Surface, clampedPage, clampedPageSize, region ?? "all", normalizedPosition ?? "any", championFilter, minGames, otpOnly, sort,
                countMs, totalSw.ElapsedMilliseconds);
            return empty;
        }

        var (pageRows, pageMs) = await TimedAsync(() => rankingResult.OrderedAccountIds is { } orderedIds
            ? LeaderboardEligibility.FetchPageByAccountIdsAsync(db, orderedIds.Skip(offset).Take(clampedPageSize).ToArray(), ct)
            : LeaderboardEligibility.FetchPageAsync(db, filter, offset, clampedPageSize, ct));
        if (pageRows.Count == 0)
        {
            // The caller asked for a page past the end. Return an empty slice
            // with the real total so the frontend's pagination control still
            // resolves to a valid range without a second round trip.
            var pastEnd = new LeaderboardResponse
            {
                Rows = Array.Empty<LeaderboardRowReadModel>(),
                Page = clampedPage,
                PageSize = clampedPageSize,
                Total = total,
            };
            cache.Set(cacheKey, pastEnd, ApiCache.Entry(ResponseCacheTtl));
            totalSw.Stop();
            logger.LogInformation(
                "{Surface} page={Page} pageSize={PageSize} region={Region} position={Position} championId={ChampionId} minGames={MinGames} otpOnly={OtpOnly} sort={Sort} rows=0 total={Total} countMs={CountMs:F1} pageMs={PageMs:F1} elapsed={ElapsedMs}ms result=past_end",
                Surface, clampedPage, clampedPageSize, region ?? "all", normalizedPosition ?? "any", championFilter, minGames, otpOnly, sort,
                total, countMs, pageMs, totalSw.ElapsedMilliseconds);
            return pastEnd;
        }

        // Hydrate the page slice with derived data. Batched queries — the
        // top-3 champions (main_champion_stats) and Games / KDA / W-L
        // (champion_aggregate_scopes) keyed by account, the latest rank cells
        // (tier/div/LP) keyed by account id, the lanes, the dedication, and
        // the dominant build per (account, champion) from the aggregate
        // schema. The heavy ordering + pagination already happened, so these
        // only touch the ~25 rows on the page.
        //
        // stats / ranks depend only on the page's puuid / id arrays, so they
        // fire immediately. The build fetch needs the actual champion ids the
        // top-3 query selects, so it chains off topChampions — but that chained
        // pair still overlaps stats / ranks, so the wall-clock hydration cost is
        // unchanged. A single DbContext is not thread-safe, so each fetch runs on
        // its own short-lived context from the factory (mirrors
        // ProfileQueryService). Each task still times its own body via TimedAsync
        // so the cache-miss log keeps the per-phase breakdown; under concurrency
        // those spans overlap, so they sum to more than the wall-clock hydration
        // time — that's expected and the point (it shows which round trip
        // dominates, not how long the phase took).
        var puuids = pageRows.Select(r => r.Puuid).ToArray();
        var accountIds = pageRows.Select(r => r.Id).ToArray();
        var accountIdByPuuid = pageRows
            .GroupBy(r => r.Puuid)
            .ToDictionary(g => g.Key, g => g.First().Id);
        // Account id → puuid, the inverse the stats fetch needs: aggregate
        // scopes are keyed by RiotAccountId, but the row is keyed by puuid.
        // Id is the PK and puuid is unique per account, so this stays 1:1.
        var puuidByAccountId = accountIdByPuuid.ToDictionary(kv => kv.Value, kv => kv.Key);

        var statsTask = TimedAsync(() => WithContextAsync(
            ctx => LeaderboardPageHydration.FetchStatsAsync(ctx, RankedQueueId, accountIds, puuidByAccountId, ct), ct));
        var ranksTask = TimedAsync(() => WithContextAsync(
            ctx => LatestRankSnapshots.FetchAsync(ctx, accountIds, ct), ct));
        // The lane derivation itself lives in MainPositions, shared with search.
        var positionsTask = TimedAsync(() => WithContextAsync(
            ctx => MainPositions.FetchAsync(ctx, platforms, puuids, ct), ct));

        // The dedication-sorted path already scored every candidate to rank
        // them, so the page's scores are in hand — only the rank-sorted path
        // pays a query, and only for the page's ~25 accounts.
        var dedicationTask = TimedAsync(() => rankingResult.DedicationByAccount is { } scored
            ? Task.FromResult(scored)
            : WithContextAsync(
                ctx => MainDedication.FetchAsync(
                    ctx, accountIds, championFilter, DateTime.UtcNow, mainAnalysisOptions.Value.PlayRateFloor, ct),
                ct));

        // Chain the build fetch off the top-3 result: it resolves the page's
        // (account, champion) pairs from the selected champions, and still runs
        // alongside stats / ranks. The metric is named `buildsContinuationMs`
        // because TimedAsync wraps the whole continuation — the wait on
        // topChampionsTask *plus* the build round trips — so it is an upper
        // bound, not the build query in isolation.
        var topChampionsTask = TimedAsync(() => WithContextAsync(
            ctx => LeaderboardPageHydration.FetchTopChampionsAsync(ctx, platforms, puuids, ct), ct));
        var buildsTask = TimedAsync(async () =>
        {
            var (topChampions, _) = await topChampionsTask;
            return await WithContextAsync(
                ctx => LeaderboardTopBuilds.FetchAsync(ctx, RankedQueueId, topChampions, accountIdByPuuid, ct), ct);
        });

        await Task.WhenAll(topChampionsTask, statsTask, ranksTask, buildsTask, positionsTask, dedicationTask);

        var (topChampionsByPuuid, topChampMs) = await topChampionsTask;
        var (statsByPuuid, statsMs) = await statsTask;
        var (ranksByAccount, ranksMs) = await ranksTask;
        var (buildsByPuuidChampion, buildsContinuationMs) = await buildsTask;
        var (positionsByPuuid, positionsMs) = await positionsTask;
        var (dedicationByAccount, dedicationMs) = await dedicationTask;

        var rows = pageRows
            .Select((row, index) => LeaderboardPageHydration.BuildRow(
                rank: offset + 1 + index,
                row,
                topChampionsByPuuid.GetValueOrDefault(row.Puuid),
                buildsByPuuidChampion,
                statsByPuuid.GetValueOrDefault(row.Puuid),
                ranksByAccount.GetValueOrDefault(row.Id),
                positionsByPuuid.GetValueOrDefault(row.Puuid),
                dedicationByAccount.GetValueOrDefault(row.Id)))
            .ToList();

        var response = new LeaderboardResponse
        {
            Rows = rows,
            Page = clampedPage,
            PageSize = clampedPageSize,
            Total = total,
        };
        cache.Set(cacheKey, response, ApiCache.Entry(ResponseCacheTtl));

        totalSw.Stop();
        logger.LogInformation(
            "{Surface} page={Page} pageSize={PageSize} region={Region} position={Position} championId={ChampionId} minGames={MinGames} otpOnly={OtpOnly} sort={Sort} rows={Rows} total={Total} countMs={CountMs:F1} pageMs={PageMs:F1} topChampMs={TopChampMs:F1} statsMs={StatsMs:F1} ranksMs={RanksMs:F1} buildsContinuationMs={BuildsContinuationMs:F1} positionsMs={PositionsMs:F1} dedicationMs={DedicationMs:F1} elapsed={ElapsedMs}ms result=miss",
            Surface, clampedPage, clampedPageSize, region ?? "all", normalizedPosition ?? "any", championFilter, minGames, otpOnly, sort,
            rows.Count, total, countMs, pageMs, topChampMs, statsMs, ranksMs, buildsContinuationMs, positionsMs, dedicationMs, totalSw.ElapsedMilliseconds);

        return response;
    }

    // Runs one hydration fetch on its own short-lived context: the page's
    // fetches run concurrently, and a single DbContext is not thread-safe.
    private async Task<T> WithContextAsync<T>(Func<TrueMainDbContext, Task<T>> fetch, CancellationToken ct)
    {
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        return await fetch(ctx);
    }

    // Times a single sub-query so the cache-miss log line can carry a per-phase
    // latency breakdown (countMs/pageMs/topChampMs/statsMs/ranksMs). The whole
    // point of #195 is knowing which of the independent SQL round trips
    // dominates on prod-shaped data, so the breakdown is part of the structured
    // log, not throwaway debug. TotalMilliseconds (fractional) because a warm
    // index scan over the page's ~25 rows can finish well under 1ms.
    private static async Task<(T Result, double ElapsedMs)> TimedAsync<T>(Func<Task<T>> query)
    {
        var sw = Stopwatch.StartNew();
        var result = await query();
        sw.Stop();
        return (result, sw.Elapsed.TotalMilliseconds);
    }

    private static LeaderboardResponse Empty(int page, int pageSize) => new()
    {
        Rows = Array.Empty<LeaderboardRowReadModel>(),
        Page = page,
        PageSize = pageSize,
        Total = 0,
    };

    private sealed record ResponseShape(
        string CacheKey,
        LeaderboardFilter Filter,
        LeaderboardSort Sort,
        int Page,
        int PageSize,
        int Offset,
        string? Region);
}
