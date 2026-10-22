using Data.BuildFacts;
using Data.Entities;

namespace Data.ItemContext;

/// <summary>
/// Replays one participant's item timeline to say which completed items it held at given
/// instants (#1750) — what an enemy had built when a decision opened.
/// </summary>
/// <remarks>
/// <para>
/// The timeline is an inventory's history, so it is replayed as one: a purchase adds the item,
/// a sale or a destruction removes one copy (the components a recipe consumes are destroyed
/// when it completes), and an undo takes back its <c>BeforeId</c> and restores its
/// <c>AfterId</c> — which covers both an undone purchase and an undone sale.
/// </para>
/// <para>
/// Only items in the <see cref="BuiltItemCatalog"/> are kept: components are replayed past
/// without ever counting, so an item counts from the event that completed it and not one
/// component earlier.
/// </para>
/// <para>
/// An event at the very instant asked about is included: the instant is a completion of the
/// participant's own, recorded at the same millisecond only when the two players shopped in
/// the same frame, and the enemy's item was then on the board as the decision opened.
/// </para>
/// </remarks>
public static class BuiltInventoryReplay
{
    /// <summary>The completed items held at <paramref name="atMs"/>, a copy per copy held.</summary>
    public static IReadOnlyList<int> At(IReadOnlyList<ItemEvent> itemEvents, int atMs, BuiltItemCatalog catalog)
        => At(itemEvents, [atMs], catalog)[0];

    /// <summary>The completed items held at each of <paramref name="instants"/>, in the order given, in one pass.</summary>
    public static IReadOnlyList<IReadOnlyList<int>> At(
        IReadOnlyList<ItemEvent> itemEvents,
        IReadOnlyList<int> instants,
        BuiltItemCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(itemEvents);
        ArgumentNullException.ThrowIfNull(instants);
        ArgumentNullException.ThrowIfNull(catalog);

        var order = Enumerable.Range(0, instants.Count).OrderBy(index => instants[index]).ToList();
        var snapshots = new IReadOnlyList<int>[instants.Count];
        var held = new List<int>();
        var next = 0;

        foreach (var itemEvent in itemEvents.OrderBy(itemEvent => itemEvent.TimestampMs))
        {
            while (next < order.Count && itemEvent.TimestampMs > instants[order[next]])
            {
                snapshots[order[next++]] = [.. held];
            }

            if (next == order.Count)
            {
                break;
            }

            Apply(held, itemEvent, catalog);
        }

        while (next < order.Count)
        {
            snapshots[order[next++]] = [.. held];
        }

        return snapshots;
    }

    private static void Apply(List<int> held, ItemEvent itemEvent, BuiltItemCatalog catalog)
    {
        switch (ItemEventTypes.Classify(itemEvent.EventType))
        {
            case ItemEventKind.Purchased:
                Add(held, itemEvent.ItemId, catalog);
                break;
            case ItemEventKind.Sold:
            case ItemEventKind.Destroyed:
                held.Remove(itemEvent.ItemId);
                break;
            case ItemEventKind.Undo:
                // The resolvers' own reading of an undo: an old row may carry the id on ItemId.
                held.Remove(itemEvent.BeforeId ?? itemEvent.ItemId);
                Add(held, itemEvent.AfterId ?? 0, catalog);
                break;
        }
    }

    private static void Add(List<int> held, int itemId, BuiltItemCatalog catalog)
    {
        if (itemId > 0 && catalog.TryGet(itemId, out _))
        {
            held.Add(itemId);
        }
    }
}
