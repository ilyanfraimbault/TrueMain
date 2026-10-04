using TrueMain.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions.Directory;

/// <summary>
/// The directory's last stage: stamps each row's <c>Tier</c> / <c>TierScore</c>,
/// lane by lane, through <see cref="ChampionTierCalculator"/>. Pure — no database,
/// only the rows the earlier stages produced.
/// </summary>
internal static class ChampionDirectoryTiering
{
    private const string Surface = ChampionSummariesQueryService.Surface;

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
    public static IReadOnlyList<ChampionSummaryReadModel> AssignTiers(
        List<ChampionSummaryReadModel> summaries, ChampionTierOptions options, ILogger logger)
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
}
