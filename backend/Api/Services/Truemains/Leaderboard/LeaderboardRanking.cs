using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Services.Champions.Scopes;
using TrueMain.Services.Truemains.Identity;

namespace TrueMain.Services.Truemains.Leaderboard;

/// <summary>
/// The ranking phase of a leaderboard request: how many accounts the filter
/// admits and, on the dedication and stat sorts, the order they rank in. The page is
/// hydrated afterwards by the query service.
/// </summary>
/// <remarks>
/// Built by <see cref="TruemainsLeaderboardQueryService"/> on its own scoped
/// dependencies. Admission and the dedication order come from
/// <see cref="LeaderboardPopulation"/>, on the request-scoped context; the stat
/// lines from a short-lived context of their own.
/// </remarks>
internal sealed class LeaderboardRanking(
    TrueMainDbContext db,
    IDbContextFactory<TrueMainDbContext> dbFactory,
    IOptions<MainAnalysisOptions> mainAnalysisOptions,
    IChampionReadCache readCache)
{
    // What the population holds of each account and its mains: a few dozen bytes each, for hundreds
    // of thousands of mains, where one unit is roughly a page response (tens of KB).
    private const int PopulationCacheSize = 256;

    // A stat line is a Guid, the score and six counters (~80 bytes with the record header).
    private const int StatLinesCacheSize = 256;

    // Past this offset a rank-sorted page reads the population's order rather than seek through
    // the filter: a fresh order is what the first pages are read for, and SQL takes seconds to skip
    // this many rows.
    private const int DeepPageOffset = 1000;

    /// <summary>
    /// Rank-sorted path: the first pages are an indexed OFFSET on <c>riot_accounts."Score"</c>, whose
    /// order is live; the eligible count only sizes the pager, so it comes from the population.
    /// </summary>
    public async Task<Result> RankByScoreAsync(LeaderboardFilter filter, int offset, CancellationToken ct)
    {
        var eligible = (await PopulationAsync(ct)).Select(filter);
        return new Result(eligible.Count, offset >= DeepPageOffset ? eligible : null);
    }

    /// <summary>
    /// Dedication-sorted path: the order is a walk over the population, and the page's own scores are
    /// read with its other cells, as on every other sort.
    /// </summary>
    public async Task<Result> RankByDedicationAsync(LeaderboardFilter filter, CancellationToken ct)
    {
        var ordered = (await PopulationAsync(ct)).RankByDedication(filter, mainAnalysisOptions.Value.PlayRateFloor);
        return new Result(ordered.Count, ordered);
    }

    /// <summary>
    /// Games / KDA / win-rate sorted path (#1737): every account's figures are read once, whatever the
    /// filter, and a filter orders the accounts it admits. Derived from other tables, so there is no
    /// column to seek. Ordering a few tens of thousands of lines takes milliseconds, paid only on a
    /// response-cache miss.
    /// </summary>
    public async Task<Result> RankByStatAsync(
        LeaderboardFilter filter,
        LeaderboardSort sort,
        int queueId,
        CancellationToken ct)
    {
        var population = await PopulationAsync(ct);

        // Shared, read-only from here: Order projects a new list and never mutates it.
        var lines = await readCache.GetOrComputeAsync(
            $"truemains:stat-lines:{filter.MinGames}",
            async token =>
            {
                await using var ctx = await dbFactory.CreateDbContextAsync(token);
                var everyAccount = new LeaderboardFilter(
                    RegionFilterParser.AllExposedPlatforms().ToArray(), null, null, filter.MinGames, false);
                return await LeaderboardStatLines.FetchAsync(ctx, everyAccount, queueId, token);
            },
            ct,
            StatLinesCacheSize);

        var eligible = population.Select(filter).ToHashSet();
        var admitted = lines.Where(line => eligible.Contains(line.AccountId)).ToList();
        return new Result(admitted.Count, LeaderboardStatLines.Order(admitted, sort));
    }

    private Task<LeaderboardPopulation> PopulationAsync(CancellationToken ct)
        => readCache.GetOrComputeAsync(
            "truemains:population",
            token => LeaderboardPopulation.LoadAsync(db, token),
            ct,
            PopulationCacheSize);

    /// <summary>
    /// What the ranking phase resolved before the page is hydrated.
    /// <see cref="OrderedAccountIds"/> is null when SQL does the ordering, on a rank-sorted board's
    /// first pages.
    /// </summary>
    public sealed record Result(int Total, IReadOnlyList<Guid>? OrderedAccountIds);
}
