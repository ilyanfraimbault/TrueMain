using Data.BuildFacts;
using Data.Entities;
using Data.ItemContext;

namespace Ingestor.Processes.Components.ItemContextAggregation;

/// <summary>
/// Reads one participant's three build decisions and pairs each item with the situations it
/// may be explained by (#1450).
/// </summary>
/// <remarks>
/// The decisions come from the same three resolvers every other build surface uses —
/// <see cref="FinalBuildResolver"/>, <see cref="BootsResolver"/> and
/// <see cref="StarterItemAnalyzer"/> — so "the item this champion built" means exactly what
/// it means in the build tree the card annotates. Restating any of them here would let the
/// sentence describe a build the panel above it does not show.
/// </remarks>
public static class ItemContextSlotResolver
{
    public static IReadOnlyDictionary<ItemContextSlot, IReadOnlyDictionary<int, IReadOnlySet<ItemContextAxis>>> Resolve(
        IReadOnlyList<ItemEvent> itemEvents,
        IReadOnlyList<int> finalItems,
        IReadOnlyDictionary<int, ItemMetadata> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var starters = StarterItemAnalyzer.Analyze(itemEvents, finalItems, metadata);
        var buildItems = FinalBuildResolver.Resolve(itemEvents, finalItems, starters.Items, metadata);
        var bootsItemId = BootsResolver.Resolve(itemEvents, finalItems, starters.Items, metadata);

        var resolved = new Dictionary<ItemContextSlot, IReadOnlyDictionary<int, IReadOnlySet<ItemContextAxis>>>();

        // A slot the game says nothing about is left out entirely, never returned empty.
        // The caller counts one denominator game per slot it is handed, so returning an
        // empty starter basket — which means "no early purchase was recorded", not "this
        // player started with nothing" — would divide every starter's pick rate by games in
        // which the question was never asked.
        Add(resolved, ItemContextSlot.Build, buildItems, metadata);
        Add(resolved, ItemContextSlot.Starter, starters.Items, metadata);
        Add(resolved, ItemContextSlot.Boots, bootsItemId > 0 ? [bootsItemId] : [], metadata);

        return resolved;
    }

    private static void Add(
        Dictionary<ItemContextSlot, IReadOnlyDictionary<int, IReadOnlySet<ItemContextAxis>>> resolved,
        ItemContextSlot slot,
        IReadOnlyList<int> itemIds,
        IReadOnlyDictionary<int, ItemMetadata> metadata)
    {
        var eligible = Eligible(itemIds, slot, metadata);
        if (eligible.Count > 0)
        {
            resolved[slot] = eligible;
        }
    }

    /// <summary>
    /// Pairs each resolved item with its whitelisted axes. An item the patch's metadata does
    /// not know is dropped rather than counted blind: without its categories there is no way
    /// to say which situations it could answer, and counting it would put a row in the table
    /// that the read could never explain.
    /// </summary>
    private static IReadOnlyDictionary<int, IReadOnlySet<ItemContextAxis>> Eligible(
        IReadOnlyList<int> itemIds,
        ItemContextSlot slot,
        IReadOnlyDictionary<int, ItemMetadata> metadata)
    {
        var eligible = new Dictionary<int, IReadOnlySet<ItemContextAxis>>();
        foreach (var itemId in itemIds)
        {
            if (itemId > 0 && metadata.TryGetValue(itemId, out var item))
            {
                eligible[itemId] = ItemContextWhitelist.For(item, slot);
            }
        }

        return eligible;
    }
}
