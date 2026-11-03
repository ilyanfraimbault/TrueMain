using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Truemains;
using TrueMain.Services.Champions.Scopes;

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
    IChampionReadCache readCache,
    ILogger logger)
{
    // Safety valve on the dedication ranking: the score is a read-time
    // expression, so ordering by it means scoring every eligible account rather
    // than seeking an index. The cap bounds that scan if the tracked population
    // ever grows by orders of magnitude; below it the ranking is exact. Rows
    // beyond the cap are the lowest play rates in the population (see
    // MainDedication.FetchCandidatesAsync), i.e. the least committed players.
    private const int MaxDedicationCandidates = 50_000;

    // A ranking is not one response: it holds the whole scored population, so
    // charging it one unit would let it evict the rest of the cache for free. One
    // unit is roughly a page response (tens of KB); a scored candidate is ~200 bytes
    // and the population is in the hundred thousands. The charge is flat because the
    // read cache takes it before the answer exists, and a filter's narrower board
    // only overpays, which costs that board an earlier eviction.
    private const int RankingCacheSize = 512;

    // A stat line is a Guid, the score and six counters (~80 bytes with the record
    // header), well under half a scored dedication candidate.
    private const int StatLinesCacheSize = 256;

    /// <summary>
    /// Rank-sorted path: only the eligible count is needed up front, the page
    /// itself is an indexed OFFSET on <c>riot_accounts."Score"</c>.
    /// </summary>
    /// <remarks>
    /// The count is nearly all of what a page of this board costs (#2425): an EXISTS over
    /// the whole eligible population, for a figure that only sizes the pager. It lives with
    /// the rankings below, per filter shape and aggregation version, rather than with the
    /// page's 30 s response.
    /// </remarks>
    public async Task<Result> CountByRankAsync(LeaderboardFilter filter, CancellationToken ct)
    {
        var total = await readCache.GetOrComputeAsync(
            $"truemains:eligible-count:{filter.Key}",
            token => LeaderboardEligibility.CountAsync(db, filter, token),
            ct);
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
    /// sorted board and aggregation version, not once per page. Everyone who misses
    /// together shares one scoring pass (#870): this path replaced an indexed count
    /// with a scored scan of up to MaxDedicationCandidates accounts.
    /// </remarks>
    public Task<Result> RankByDedicationAsync(LeaderboardFilter filter, CancellationToken ct)
        => readCache.GetOrComputeAsync(
            $"truemains:dedication-ranking:{filter.Key}",
            token => ComputeDedicationRankingAsync(filter, token),
            ct,
            RankingCacheSize);

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
        CancellationToken ct)
    {
        // Shared, read-only from here: Order projects a new list and never mutates it.
        var lines = await readCache.GetOrComputeAsync(
            $"truemains:stat-lines:{filter.Key}",
            async token =>
            {
                await using var ctx = await dbFactory.CreateDbContextAsync(token);
                return await LeaderboardStatLines.FetchAsync(ctx, filter, queueId, token);
            },
            ct,
            StatLinesCacheSize);

        return new Result(
            Total: lines.Count,
            OrderedAccountIds: LeaderboardStatLines.Order(lines, sort),
            DedicationByAccount: null);
    }

    private async Task<Result> ComputeDedicationRankingAsync(LeaderboardFilter filter, CancellationToken ct)
    {
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

        // Caching the whole Result (not just the ids) is what keeps Truncated's
        // consequences correct across pages: Total already folds in the extra
        // CountAsync, and the migrate-to-a-materialised-column warning above sits
        // on this miss path, so it fires once per ranking rather than once per
        // page — and is never lost, since a cache hit means the same truncation
        // verdict still applies. Shared, read-only from here: the query service
        // only slices OrderedAccountIds and looks up DedicationByAccount.
        return new Result(
            Total: total,
            OrderedAccountIds: candidates.Select(candidate => candidate.AccountId).ToList(),
            DedicationByAccount: candidates.ToDictionary(
                candidate => candidate.AccountId,
                candidate => candidate.Dedication));
    }

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
