using Microsoft.Extensions.Options;
using TrueMain.Options;
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
/// The volume chips span more than the teaser does (#1109): they sum
/// <c>ChampionsList:HomepagePatchWindow</c> patches back from the served one, so
/// the headline figure does not collapse on patch day, while the teaser stays on
/// the served patch alone because a tier is a percentile within one patch's field.
/// The two are computed from different calls for exactly that reason.
/// </para>
/// </summary>
public sealed class ChampionOverviewQueryService(
    IChampionSummariesQueryService summariesQueryService,
    IOptions<ChampionsListOptions> championsOptions) : IChampionOverviewQueryService
{
    public async Task<ChampionOverviewReadModel> GetOverviewAsync(int limit, CancellationToken ct)
    {
        var result = await summariesQueryService.GetAllSummariesAsync(patch: null, eloBracket: null, ct);

        var volumes = await summariesQueryService.GetServedPatchVolumesAsync(
            Math.Max(1, championsOptions.Value.HomepagePatchWindow), ct);

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

        // Fall back to the served patch's own totals when the window came back empty
        // (no aggregate data at all): the chips then read exactly as they did before
        // the window existed, rather than reading zero beside a populated teaser.
        return volumes.Count == 0
            ? new ChampionOverviewReadModel
            {
                PatchVersion = result.PatchVersion,
                GamesAnalyzed = result.TotalGames,
                ChampionsRanked = result.ChampionsRanked,
                CountedPatches = string.IsNullOrEmpty(result.PatchVersion) ? [] : [result.PatchVersion],
                TopRows = topRows,
            }
            : new ChampionOverviewReadModel
            {
                PatchVersion = result.PatchVersion,
                GamesAnalyzed = volumes.Sum(volume => volume.TotalGames),
                // Distinct across the window, not summed: a champion ranked on both
                // patches is one champion, and summing would double-count almost all
                // of them.
                ChampionsRanked = volumes
                    .SelectMany(volume => volume.ChampionsPastFloor)
                    .Distinct()
                    .Count(),
                CountedPatches = [.. volumes.Select(volume => volume.Patch)],
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
