using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains.Leaderboard;

/// <summary>
/// The ranking phase of a leaderboard request: how many accounts the filter
/// admits and, on the dedication and stat sorts, the order they rank in. The page is
/// hydrated afterwards by the query service.
/// </summary>
/// <remarks>
/// Built by <see cref="TruemainsLeaderboardQueryService"/> on its own scoped
/// dependencies; the count runs on the request-scoped context, the dedication
/// scan on a short-lived one of its own.
/// </remarks>
internal sealed class LeaderboardRanking(
    TrueMainDbContext db,
    IDbContextFactory<TrueMainDbContext> dbFactory,
    IOptions<MainAnalysisOptions> mainAnalysisOptions,
    IMemoryCache cache,
    ILogger logger)
{
    // Static because the owning service is scoped: the whole point is to
    // coalesce across concurrent *requests*, which each get their own instance.
    private static readonly RequestCoalescer<Result> Coalescer = new();

    private static readonly RequestCoalescer<List<LeaderboardStatLines.Line>> StatLinesCoalescer = new();

    // Safety valve on the dedication ranking: the score is a read-time
    // expression, so ordering by it means scoring every eligible account rather
    // than seeking an index. The cap bounds that scan if the tracked population
    // ever grows by orders of magnitude; below it the ranking is exact. Rows
    // beyond the cap are the lowest play rates in the population (see
    // MainDedication.FetchCandidatesAsync), i.e. the least committed players.
    private const int MaxDedicationCandidates = 50_000;

    // A ranking entry is not one response — it holds the whole scored
    // population, so charging it 1 unit would let it evict the rest of the cache
    // for free. One unit is roughly a page response (~25 hydrated rows, tens of
    // KB); a scored candidate is ~200 bytes across the id list, the dictionary
    // and the read model, so ~125 candidates cost about what one response does.
    // Round to 100 per unit to charge slightly over the odds rather than under.
    private const int CandidatesPerCacheUnit = 100;

    // Cap one ranking at an eighth of the total budget. Beyond that the entry
    // would distort eviction for every other surface sharing this cache, so the
    // ranking simply is not cached and each page rescans — the same behaviour as
    // before this cache existed. At the current ratio this covers populations up
    // to ~12 800 accounts, comfortably above real scale; a board big enough to
    // exceed it is already past the point where the score should be materialised
    // rather than derived per request.
    private const int MaxRankingCacheSize = 128;

    // A stat line is a Guid, the score and six counters (~80 bytes with the
    // record header), well under half a scored dedication candidate, so it is
    // charged at 250 per unit. Under the same MaxRankingCacheSize cap that
    // keeps populations up to ~32 000 accounts cached.
    private const int StatLinesPerCacheUnit = 250;

    /// <summary>
    /// Rank-sorted path: only the eligible count is needed up front, the page
    /// itself is an indexed OFFSET on <c>riot_accounts."Score"</c>.
    /// </summary>
    public async Task<Result> CountByRankAsync(LeaderboardFilter filter, CancellationToken ct)
    {
        var total = await LeaderboardEligibility.CountAsync(db, filter, ct);
        return new Result(total, OrderedAccountIds: null, DedicationByAccount: null);
    }

    /// <summary>
    /// Dedication-sorted path: score every eligible account, keep the ordering
    /// and the scores so the page slice needs neither a second scoring pass nor
    /// a separate count.
    /// </summary>
    /// <remarks>
    /// The result is cached per filter shape, without the page — paging is the
    /// normal way people use a leaderboard, and the per-page response cache
    /// alone would make every page change repeat the full scan and scoring pass.
    /// The read-time design is defensible because that cost is paid once per
    /// sorted board, not once per page.
    /// </remarks>
    public async Task<Result> RankByDedicationAsync(
        LeaderboardFilter filter,
        TimeSpan cacheTtl,
        CancellationToken ct)
    {
        var rankingCacheKey = BuildCacheKey(filter);
        if (cache.TryGetValue<Result>(rankingCacheKey, out var cachedRanking) && cachedRanking is not null)
        {
            // Shared instance, read-only from here: the query service only
            // slices OrderedAccountIds and looks up DedicationByAccount. Neither
            // is ever mutated, so handing the same object to concurrent requests
            // is safe — keep it that way if this grows.
            return cachedRanking;
        }

        // Miss. Everyone who missed together shares one scoring pass instead of each
        // running their own over the eligible population (#870) — the reason this path
        // needs it more than the others is that the dedication sort replaced an indexed
        // count with a scored scan of up to MaxDedicationCandidates accounts.
        return await Coalescer.GetOrJoinAsync(
            rankingCacheKey,
            () => ComputeAndCacheAsync(rankingCacheKey, filter, cacheTtl),
            ct);
    }

    /// <summary>
    /// Games / KDA / win-rate sorted path (#1737): read every eligible
    /// account's figures once, then order them in memory. Like the dedication
    /// ranking, the figures are derived from other tables, so there is no
    /// column to seek and the cost is one scan per sorted board.
    /// </summary>
    /// <remarks>
    /// The scan is cached per filter shape and shared by the three stat sorts:
    /// switching from Games to KDA on the same filters reorders cached lines
    /// instead of rescanning. Ordering a few tens of thousands of lines takes
    /// milliseconds, paid only on a response-cache miss.
    /// </remarks>
    public async Task<Result> RankByStatAsync(
        LeaderboardFilter filter,
        LeaderboardSort sort,
        int queueId,
        TimeSpan cacheTtl,
        CancellationToken ct)
    {
        var cacheKey = $"truemains:stat-lines:{filter.Key}";
        if (!cache.TryGetValue<List<LeaderboardStatLines.Line>>(cacheKey, out var lines) || lines is null)
        {
            lines = await StatLinesCoalescer.GetOrJoinAsync(
                cacheKey,
                () => FetchAndCacheStatLinesAsync(cacheKey, filter, queueId, cacheTtl),
                ct);
        }

        return new Result(
            Total: lines.Count,
            OrderedAccountIds: LeaderboardStatLines.Order(lines, sort),
            DedicationByAccount: null);
    }

    private async Task<List<LeaderboardStatLines.Line>> FetchAndCacheStatLinesAsync(
        string cacheKey,
        LeaderboardFilter filter,
        int queueId,
        TimeSpan cacheTtl)
    {
        // Detached from any single caller's token: the pass is shared (see
        // RequestCoalescer).
        var ct = CancellationToken.None;

        // Re-check under the coalescer, as the dedication path does.
        if (cache.TryGetValue<List<LeaderboardStatLines.Line>>(cacheKey, out var justCached) && justCached is not null)
        {
            return justCached;
        }

        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        var lines = await LeaderboardStatLines.FetchAsync(ctx, filter, queueId, ct);

        // Shared, read-only from here: Order projects a new list and never
        // mutates the cached one.
        var size = Math.Max(1, lines.Count / StatLinesPerCacheUnit);
        if (size <= MaxRankingCacheSize)
        {
            cache.Set(cacheKey, lines, ApiCache.Entry(cacheTtl, size));
        }

        return lines;
    }

    private async Task<Result> ComputeAndCacheAsync(
        string rankingCacheKey,
        LeaderboardFilter filter,
        TimeSpan cacheTtl)
    {
        // Detached from any single caller's token: the pass is shared, so one request
        // walking away must not cancel it for the others (see RequestCoalescer). It is
        // bounded by the candidate cap and its own TTL.
        var ct = CancellationToken.None;

        // Re-check under the coalescer: while this caller was queueing behind another
        // pass for the same key, that pass may have finished and cached its result.
        if (cache.TryGetValue<Result>(rankingCacheKey, out var justCached) && justCached is not null)
        {
            return justCached;
        }

        // Own short-lived context: consistent with the other fetches, and this
        // one can be a long-running scan.
        await using var ctx = await dbFactory.CreateDbContextAsync(ct);
        var (candidates, truncated) = await MainDedication.FetchCandidatesAsync(
            ctx, filter.Platforms, filter.ChampionId, filter.Position, filter.MinGames, filter.OtpOnly,
            LeaderboardEligibility.MinPositionShare, DateTime.UtcNow, mainAnalysisOptions.Value.PlayRateFloor,
            MaxDedicationCandidates, ct);

        if (truncated)
        {
            // Not an error: the ranking is still exact for every row above the
            // cap. It does mean the deep tail is unreachable, which is the
            // signal that the score should move to a materialised column. The
            // flag comes from a probe row past the cap, so this fires only when
            // eligible accounts were genuinely left out — never when the
            // population happens to land exactly on the cap.
            logger.LogWarning(
                "{Surface} dedication ranking hit the candidate cap ({Cap}); the tail of the ranking is truncated.",
                TruemainsLeaderboardQueryService.Surface, MaxDedicationCandidates);
        }

        // `total` is a count of eligible *players*, not of reachable rows, and
        // the homepage renders it as a "truemains tracked" figure — so a
        // truncated candidate set must not silently under-report it. On the
        // truncated path pay one extra Count to keep the number true; the
        // consequence is that the unreachable tail pages come back empty (the
        // past-end branch already returns an empty slice with the real total),
        // which is the honest failure mode: a page that can't be filled beats a
        // population figure that is quietly wrong. Otherwise — i.e. always, in
        // practice — the candidate count *is* the count, no extra query.
        var total = truncated
            ? await LeaderboardEligibility.CountAsync(db, filter, ct)
            : candidates.Count;

        var ranking = new Result(
            Total: total,
            OrderedAccountIds: candidates.Select(candidate => candidate.AccountId).ToList(),
            DedicationByAccount: candidates.ToDictionary(
                candidate => candidate.AccountId,
                candidate => candidate.Dedication));

        // Caching the whole Result (not just the ids) is what keeps Truncated's
        // consequences correct across pages: Total already folds in the extra
        // CountAsync, and the migrate-to-a-materialised-column warning above sits
        // on this miss path, so it fires once per ranking rather than once per
        // page — and is never lost, since a cache hit means the same truncation
        // verdict still applies.
        var size = Math.Max(1, candidates.Count / CandidatesPerCacheUnit);
        if (size <= MaxRankingCacheSize)
        {
            cache.Set(rankingCacheKey, ranking, ApiCache.Entry(cacheTtl, size));
        }

        return ranking;
    }

    /// <summary>
    /// Key for the dedication ranking. Deliberately carries no page, page size
    /// or sort: the ranking is a property of the filter shape alone, so paging
    /// through one sorted board reuses a single scan instead of rescoring the
    /// whole population per page.
    /// </summary>
    private static string BuildCacheKey(LeaderboardFilter filter)
        => $"truemains:dedication-ranking:{filter.Key}";

    /// <summary>
    /// What the ranking phase resolved before the page is hydrated.
    /// <see cref="OrderedAccountIds"/> and <see cref="DedicationByAccount"/> are
    /// null on the rank-sorted path, where SQL does the ordering;
    /// <see cref="DedicationByAccount"/> is null on every path but the
    /// dedication sort, and the page's dedication is then fetched per slice.
    /// </summary>
    public sealed record Result(
        int Total,
        IReadOnlyList<Guid>? OrderedAccountIds,
        Dictionary<Guid, DedicationReadModel>? DedicationByAccount);
}
