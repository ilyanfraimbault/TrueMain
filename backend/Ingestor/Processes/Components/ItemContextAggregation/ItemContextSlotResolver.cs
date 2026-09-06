using Data.BuildFacts;
using Data.Entities;
using Data.ItemContext;

namespace Ingestor.Processes.Components.ItemContextAggregation;

/// <summary>
/// One step of one build, as the fold counts it (#1496): the item a game continued into,
/// the item it continued <em>from</em>, and the situations that step may be explained by.
/// </summary>
/// <param name="ParentItemId">The completed item this decision followed, 0 on the branch every game starts on.</param>
/// <param name="ItemId">The item the game went on to complete.</param>
/// <param name="EligibleAxes">The axes this item could mechanically answer (<see cref="ItemContextWhitelist"/>).</param>
public readonly record struct ItemContextEdge(
    int ParentItemId,
    int ItemId,
    IReadOnlySet<ItemContextAxis> EligibleAxes);

/// <summary>
/// Reads one participant's three build decisions and turns each into the steps the fold
/// counts (#1450), each paired with the situations it may be explained by.
/// </summary>
/// <remarks>
/// <para>
/// The decisions come from the same three resolvers every other build surface uses —
/// <see cref="FinalBuildResolver"/>, <see cref="BootsResolver"/> and
/// <see cref="StarterItemAnalyzer"/> — so "the item this champion built" means exactly what
/// it means in the build tree the card annotates. Restating any of them here would let the
/// sentence describe a build the panel above it does not show.
/// </para>
/// <para>
/// <b>The build slot is a chain, the other two are one step (#1496).</b> A completed
/// legendary is only ever chosen after the previous one, so the build items come out as the
/// consecutive edges of the progression — the same edges the tree on the page draws. Boots
/// and starters are decided before any item exists, so they all hang off the root branch,
/// where they are each other's alternatives exactly as the panel presents them.
/// </para>
/// </remarks>
public static class ItemContextSlotResolver
{
    public static IReadOnlyDictionary<ItemContextSlot, IReadOnlyList<ItemContextEdge>> Resolve(
        IReadOnlyList<ItemEvent> itemEvents,
        IReadOnlyList<int> finalItems,
        IReadOnlyDictionary<int, ItemMetadata> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var starters = StarterItemAnalyzer.Analyze(itemEvents, finalItems, metadata);
        var buildItems = FinalBuildResolver.Resolve(itemEvents, finalItems, starters.Items, metadata);
        var bootsItemId = BootsResolver.Resolve(itemEvents, finalItems, starters.Items, metadata);

        var resolved = new Dictionary<ItemContextSlot, IReadOnlyList<ItemContextEdge>>();

        // A slot the game says nothing about is left out entirely, never returned empty.
        // The caller counts one denominator game per branch it is handed, so returning an
        // empty starter basket — which means "no early purchase was recorded", not "this
        // player started with nothing" — would divide every starter's pick rate by games in
        // which the question was never asked.
        Add(resolved, ItemContextSlot.Build, Chain(buildItems, metadata));
        Add(resolved, ItemContextSlot.Starter, Root(starters.Items, ItemContextSlot.Starter, metadata));
        Add(resolved, ItemContextSlot.Boots, Root(bootsItemId > 0 ? [bootsItemId] : [], ItemContextSlot.Boots, metadata));

        return resolved;
    }

    private static void Add(
        Dictionary<ItemContextSlot, IReadOnlyList<ItemContextEdge>> resolved,
        ItemContextSlot slot,
        IReadOnlyList<ItemContextEdge> edges)
    {
        if (edges.Count > 0)
        {
            resolved[slot] = edges;
        }
    }

    /// <summary>
    /// The build progression as consecutive edges. An item the patch's metadata does not
    /// know <b>breaks the chain rather than being skipped over</b>: pretending the item
    /// after it followed the item before it would invent a step the game never took, and
    /// the branch it would land on is one a reader could not see on the tree either.
    /// </summary>
    private static List<ItemContextEdge> Chain(
        IReadOnlyList<int> itemIds,
        IReadOnlyDictionary<int, ItemMetadata> metadata)
    {
        var edges = new List<ItemContextEdge>();
        var parentItemId = 0;

        foreach (var itemId in itemIds)
        {
            if (itemId <= 0 || !metadata.TryGetValue(itemId, out var item))
            {
                break;
            }

            edges.Add(new ItemContextEdge(parentItemId, itemId, ItemContextWhitelist.For(item, ItemContextSlot.Build)));
            parentItemId = itemId;
        }

        return edges;
    }

    /// <summary>
    /// A slot whose items are all decided at once, before any completed item exists: they
    /// hang off the root branch and are each other's alternatives. An item the patch's
    /// metadata does not know is dropped rather than counted blind — without its categories
    /// there is no way to say which situations it could answer.
    /// </summary>
    private static List<ItemContextEdge> Root(
        IReadOnlyList<int> itemIds,
        ItemContextSlot slot,
        IReadOnlyDictionary<int, ItemMetadata> metadata)
    {
        var edges = new List<ItemContextEdge>();
        foreach (var itemId in itemIds)
        {
            if (itemId > 0 && metadata.TryGetValue(itemId, out var item))
            {
                edges.Add(new ItemContextEdge(0, itemId, ItemContextWhitelist.For(item, slot)));
            }
        }

        return edges;
    }
}
