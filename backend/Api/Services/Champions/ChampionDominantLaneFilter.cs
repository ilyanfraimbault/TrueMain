using TrueMain.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Cuts the champion directory down to each champion's dominant lanes (#1082).
///
/// <para>
/// One <c>champion_aggregate_scopes</c> row exists per <c>(champion, lane)</c>
/// the population actually played, and champions flex: on patch 16.15, 173
/// champions produced 561 directory lines, up to five for one champion. The
/// list then answers "which champion-lane pairs exist" when the question asked
/// is "which champions are strong" — Ahri five times is five rows of the same
/// answer, and the four extra ones are off-role noise: every line past a
/// champion's top two carried, together, 5.9% of that champion's games.
/// </para>
///
/// <para>
/// The rule is therefore: keep a champion's most-played lane always, and its
/// next lane only if that lane carries <see cref="ChampionsListOptions.MinSecondaryLanePlayRate"/>
/// of the champion's own games, up to <see cref="ChampionsListOptions.MaxLanesPerChampion"/>
/// lines. Keeping the top lane unconditionally is what stops a genuine
/// five-lane flex (five lanes at 20%, none of them "dominant") from
/// disappearing out of a list of champions entirely.
/// </para>
///
/// <para>
/// This runs <b>before</b> tiering, not after: the tier is a percentile within
/// a lane, so leaving the off-role lines in the field would rank each champion
/// against picks nobody plays there. It runs <b>after</b> the
/// <see cref="ChampionsListOptions.MinSampleGames"/> floor for the same reason
/// — a lane that never cleared the sample floor is not evidence of a second
/// identity, so it must not consume one of the two slots.
/// </para>
/// </summary>
public static class ChampionDominantLaneFilter
{
    /// <summary>
    /// Returns <paramref name="summaries"/> with each champion reduced to its
    /// dominant lanes, preserving the input order of the rows it keeps.
    ///
    /// <para>
    /// Ranking is by games desc, then lane name, so two lanes tied on games
    /// resolve the same way on every request — the payload is cached and
    /// served to everyone, and a list that reshuffles between two identical
    /// requests reads as a data change.
    /// </para>
    /// </summary>
    public static IReadOnlyList<ChampionSummaryReadModel> KeepDominantLanes(
        IReadOnlyList<ChampionSummaryReadModel> summaries, ChampionsListOptions options)
    {
        if (options.MaxLanesPerChampion <= 0 || summaries.Count == 0)
        {
            return summaries;
        }

        var kept = new HashSet<(int ChampionId, string Position)>();

        foreach (var champion in summaries.GroupBy(summary => summary.ChampionId))
        {
            var lanes = champion
                .OrderByDescending(summary => summary.Games)
                .ThenBy(summary => summary.Position, StringComparer.Ordinal)
                .ToList();

            for (var rank = 0; rank < lanes.Count && rank < options.MaxLanesPerChampion; rank++)
            {
                // Rank 0 is the champion's main lane by definition — it is kept
                // whatever its share, so no champion is filtered out of the
                // champion list. Every other lane has to earn its slot.
                if (rank > 0 && lanes[rank].LanePlayRate < options.MinSecondaryLanePlayRate)
                {
                    break;
                }

                kept.Add((lanes[rank].ChampionId, lanes[rank].Position));
            }
        }

        return summaries
            .Where(summary => kept.Contains((summary.ChampionId, summary.Position)))
            .ToList();
    }
}
