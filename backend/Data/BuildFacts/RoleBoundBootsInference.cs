using Data.Entities;

namespace Data.BuildFacts;

/// <summary>
/// Recovers what Riot's role-bound slot held for a bot laner whose row was ingested
/// before the slot was recorded (#1612). A bot laner's boots always live in that slot,
/// so the pair they ended with is the last pair they bought. When they bought none,
/// the last pair any item event names is used instead: Slightly Magical Footwear is
/// granted by a rune, not bought. Measured against rows where Riot's value is known,
/// the purchase rule alone matched 99 % of them, and every miss was that rune's boots.
/// </summary>
public static class RoleBoundBootsInference
{
    /// <summary>
    /// The inferred boots, or 0 when the inventory slots already hold boots (the rare
    /// bot laner whose boots never moved) or the timeline names none.
    /// </summary>
    public static int Infer(
        IReadOnlyList<ItemEvent> itemEvents,
        IReadOnlyList<int> inventorySlots,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
    {
        if (inventorySlots.Any(itemId => IsBoots(itemId, itemMetadataById)))
        {
            return 0;
        }

        var purchases = new List<int>();
        var lastSeen = 0;

        foreach (var itemEvent in itemEvents.OrderBy(itemEvent => itemEvent.TimestampMs))
        {
            switch (ItemEventTypes.Classify(itemEvent.EventType))
            {
                case ItemEventKind.Purchased when IsBoots(itemEvent.ItemId, itemMetadataById):
                    purchases.Add(itemEvent.ItemId);
                    break;
                case ItemEventKind.Undo when itemEvent.BeforeId is { } undone:
                    var index = purchases.LastIndexOf(undone);
                    if (index >= 0)
                    {
                        purchases.RemoveAt(index);
                    }
                    break;
            }

            if (IsBoots(itemEvent.ItemId, itemMetadataById))
            {
                lastSeen = itemEvent.ItemId;
            }
        }

        return purchases.Count > 0 ? purchases[^1] : lastSeen;
    }

    private static bool IsBoots(int itemId, IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
        => itemId > 0
           && itemMetadataById.TryGetValue(itemId, out var metadata)
           && metadata.IsBootsItem;
}
