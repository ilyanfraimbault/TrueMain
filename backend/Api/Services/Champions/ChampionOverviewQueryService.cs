using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Builds the homepage's champion overview (#972). Owns no SQL of its own —
/// reads the same cached <see cref="IChampionSummariesQueryService.GetAllSummariesAsync"/>
/// entry as an unqualified <c>GET /champions</c>, so the homepage's teaser and
/// the directory never disagree, and the homepage never pays for a second
/// aggregate computation.
///
/// <para>
/// The volume chip spans far more than the teaser does: it is the lifetime total
/// over every patch the aggregate table holds, while the teaser stays on the served
/// patch alone because a tier is a percentile within one patch's field. The two come
/// from different calls for exactly that reason.
/// </para>
/// </summary>
public sealed class ChampionOverviewQueryService(
    IChampionSummariesQueryService summariesQueryService,
    IChampionReadCache cache) : IChampionOverviewQueryService
{
    public Task<ChampionOverviewReadModel> GetOverviewAsync(int limit, CancellationToken ct)
        // The summaries underneath are cached too, but the homepage teaser is a sort
        // and a trim over ~900 lines on top of them — cheap, and pointless to repeat
        // for every visitor between two aggregation cycles.
        => cache.GetOrComputeAsync(
            $"champions:overview:{limit}",
            token => ComputeOverviewAsync(limit, token),
            ct);

    private async Task<ChampionOverviewReadModel> ComputeOverviewAsync(int limit, CancellationToken ct)
    {
        // Truemains, always: the homepage renders no population toggle, and its
        // teaser has to agree with the chip beside it, which counts main games.
        var result = await summariesQueryService.GetAllSummariesAsync(
            patch: null, eloBracket: null, truemainsOnly: true, ct);

        var gamesAnalyzed = await summariesQueryService.GetTotalGamesAsync(ct);

        // Best tier first, then games as the tiebreaker. Winrate would be the
        // obvious second key, but it floats micro-sample rows (a 90% WR off-lane
        // pick with a handful of games) to the very top — the opposite of the
        // "honest sample sizes" promise the homepage teaser makes. Most-played
        // S-tiers read as the meta. Mirrors the ordering the homepage teaser used
        // to do client-side (home/TierlistPanel.vue) before this endpoint existed.
        var topRows = result.Summaries
            .OrderBy(summary => TierRank(summary.Tier))
            .ThenByDescending(summary => summary.Games)
            .Take(limit)
            .Select(summary => new ChampionOverviewRowReadModel
            {
                ChampionId = summary.ChampionId,
                Position = summary.Position,
                Tier = summary.Tier,
                Games = summary.Games,
                WinRate = summary.WinRate,
                PickRate = summary.PickRate,
                BanRate = summary.BanRate,
            })
            .ToList();

        return new ChampionOverviewReadModel
        {
            PatchVersion = result.PatchVersion,
            GamesAnalyzed = gamesAnalyzed,
            TopRows = topRows,
        };
    }

    // Unrecognised tiers (shouldn't happen — every summary row is stamped by
    // ChampionTierCalculator) sort last rather than throwing.
    private static int TierRank(string tier)
    {
        var index = Array.IndexOf(ChampionTierCalculator.TierOrder, tier);
        return index < 0 ? ChampionTierCalculator.TierOrder.Length : index;
    }
}
