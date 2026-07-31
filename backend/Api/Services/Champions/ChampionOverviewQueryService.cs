using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Builds the homepage's champion overview (#972). Owns no SQL of its own —
/// reads the same cached <see cref="IChampionSummariesQueryService.GetAllSummariesAsync"/>
/// entry as an unqualified <c>GET /champions</c>, so the homepage's stat chip
/// and teaser share the exact totals the directory itself reads, and the
/// homepage never pays for a second aggregate computation.
/// </summary>
public sealed class ChampionOverviewQueryService(
    IChampionSummariesQueryService summariesQueryService) : IChampionOverviewQueryService
{
    // Best tier first, then games as the tiebreaker. Winrate would be the
    // obvious second key, but it floats micro-sample rows (a 90% WR off-lane
    // pick with a handful of games) to the very top — the opposite of the
    // "honest sample sizes" promise the homepage teaser makes. Most-played
    // S-tiers read as the meta. Mirrors the ordering the homepage teaser used
    // to do client-side (home/TierlistPanel.vue) before this endpoint existed.
    private static readonly string[] TierOrder =
    [
        ChampionTierCalculator.TierS,
        ChampionTierCalculator.TierA,
        ChampionTierCalculator.TierB,
        ChampionTierCalculator.TierC,
        ChampionTierCalculator.TierD,
    ];

    public async Task<ChampionOverviewReadModel> GetOverviewAsync(int limit, CancellationToken ct)
    {
        var result = await summariesQueryService.GetAllSummariesAsync(patch: null, eloBracket: null, ct);

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
            GamesAnalyzed = result.TotalGames,
            ChampionsRanked = result.ChampionsRanked,
            TopRows = topRows,
        };
    }

    // Unrecognised tiers (shouldn't happen — every summary row is stamped by
    // ChampionTierCalculator) sort last rather than throwing.
    private static int TierRank(string tier)
    {
        var index = Array.IndexOf(TierOrder, tier);
        return index < 0 ? TierOrder.Length : index;
    }
}
