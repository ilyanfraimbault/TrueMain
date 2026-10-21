using Data.Entities;
using Data.ItemContext;

namespace Ingestor.Processes.Components.ItemContextAggregation;

/// <summary>
/// Places each build decision of one participant on the in-game axes (#1750): what its five
/// enemies — and its lane opponent — had completed when the decision opened.
/// </summary>
/// <remarks>
/// <para>
/// <b>The root branch carries none.</b> Its decision opens at 0:00, when nobody has built
/// anything: every game would sit in the same bucket, so the axis could never contrast two
/// ends, and counting it would only add rows. The same goes for the boots and the starter,
/// which hang off the root.
/// </para>
/// <para>
/// <b>A branch whose instant is unknown carries none either</b> — the parent read off the
/// final inventory with no purchase on the timeline. Guessing an instant would place the game
/// in a bucket nobody measured it in.
/// </para>
/// </remarks>
public static class BuiltBranchAxes
{
    private static readonly IReadOnlyDictionary<int, IReadOnlyDictionary<ItemContextAxis, ItemContextBucket>> None =
        new Dictionary<int, IReadOnlyDictionary<ItemContextAxis, ItemContextBucket>>();

    /// <summary>The in-game buckets of each build branch of one participant.</summary>
    /// <param name="buildEdges">The participant's build-slot edges (<see cref="ItemContextSlotResolver"/>).</param>
    /// <param name="enemies">The five enemies' timelines, or null when one of them is missing.</param>
    /// <param name="laneOpponent">The lane opponent's timeline, or null when there is none or it is missing.</param>
    /// <param name="catalog">The patch's completed items.</param>
    /// <param name="thresholds">The bands.</param>
    /// <param name="distribution">Where the raw values are recorded, for calibrating the bands.</param>
    /// <returns>The buckets of each branch, by parent item.</returns>
    public static IReadOnlyDictionary<int, IReadOnlyDictionary<ItemContextAxis, ItemContextBucket>> Resolve(
        IReadOnlyList<ItemContextEdge> buildEdges,
        IReadOnlyList<IReadOnlyList<ItemEvent>>? enemies,
        IReadOnlyList<ItemEvent>? laneOpponent,
        BuiltItemCatalog catalog,
        BuiltAxisThresholds thresholds,
        BuiltAxisDistribution distribution)
    {
        ArgumentNullException.ThrowIfNull(buildEdges);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(distribution);

        if (catalog.Count == 0 || (enemies is null && laneOpponent is null))
        {
            return None;
        }

        var branches = buildEdges
            .Where(edge => edge.ParentItemId > 0 && edge.DecisionAtMs is > 0)
            .Select(edge => (edge.ParentItemId, AtMs: edge.DecisionAtMs!.Value))
            .Distinct()
            .ToList();
        if (branches.Count == 0)
        {
            return None;
        }

        var instants = branches.Select(branch => branch.AtMs).ToList();
        var enemyInventories = enemies?.Select(timeline => BuiltInventoryReplay.At(timeline, instants, catalog)).ToList();
        var opponentInventories = laneOpponent is null ? null : BuiltInventoryReplay.At(laneOpponent, instants, catalog);

        var resolved = new Dictionary<int, IReadOnlyDictionary<ItemContextAxis, ItemContextBucket>>();
        for (var i = 0; i < branches.Count; i++)
        {
            var at = i;
            var context = new BuiltContext(
                enemyInventories?.Select(inventory => inventory[at]).ToList(),
                opponentInventories?[at]);

            var values = BuiltAxisEvaluator.Measure(context, catalog, thresholds);
            distribution.Add(values);
            resolved[branches[i].ParentItemId] = BuiltAxisEvaluator.Bucket(values, thresholds);
        }

        return resolved;
    }
}
