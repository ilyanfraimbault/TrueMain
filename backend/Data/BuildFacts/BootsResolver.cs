using Data.Entities;

namespace Data.BuildFacts;

public static class BootsResolver
{
    public static int Resolve(
        IReadOnlyList<ItemEvent> itemEvents,
        IReadOnlyList<int> finalItems,
        IReadOnlyCollection<int> starterItems,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
    {
        var starterItemIds = starterItems
            .Where(itemId => itemId > 0)
            .ToHashSet();

        var purchasedBoots = BuildBootPurchaseTimeline(itemEvents, starterItemIds, itemMetadataById)
            .OrderByDescending(candidate => candidate.TimestampMs)
            .ThenByDescending(candidate => candidate.Metadata.IsFinalBoots)
            .ThenByDescending(candidate => candidate.Metadata.PriceTotal)
            .ThenBy(candidate => candidate.Metadata.Id)
            .Select(candidate => candidate.Metadata.Id)
            .FirstOrDefault();

        if (purchasedBoots > 0)
        {
            return purchasedBoots;
        }

        return finalItems
            .Where(itemId => itemId > 0)
            .Where(itemId => !starterItemIds.Contains(itemId))
            .Distinct()
            .Select(itemId => itemMetadataById.TryGetValue(itemId, out var metadata) ? metadata : null)
            .Where(metadata => metadata is { InStore: true, IsConsumable: false, IsBootsItem: true })
            .OrderByDescending(metadata => metadata!.IsFinalBoots)
            .ThenByDescending(metadata => metadata!.PriceTotal)
            .ThenBy(metadata => metadata!.Id)
            .Select(metadata => metadata!.Id)
            .FirstOrDefault();
    }

    private static IReadOnlyCollection<CandidateBootPurchase> BuildBootPurchaseTimeline(
        IReadOnlyList<ItemEvent> itemEvents,
        IReadOnlySet<int> starterItemIds,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
    {
        var purchasedBoots = new List<CandidateBootPurchase>();

        foreach (var itemEvent in itemEvents.OrderBy(itemEvent => itemEvent.TimestampMs))
        {
            switch (itemEvent.EventType.ToUpperInvariant())
            {
                case "ITEM_PURCHASED":
                    TryAddBootPurchaseCandidate(purchasedBoots, itemEvent.ItemId, itemEvent.TimestampMs, starterItemIds, itemMetadataById);
                    break;
                case "ITEM_UNDO":
                    RemoveBootPurchaseCandidate(purchasedBoots, itemEvent.BeforeId ?? itemEvent.ItemId);
                    TryAddBootPurchaseCandidate(purchasedBoots, itemEvent.AfterId, itemEvent.TimestampMs, starterItemIds, itemMetadataById);
                    break;
            }
        }

        return purchasedBoots;
    }

    private static void TryAddBootPurchaseCandidate(
        ICollection<CandidateBootPurchase> purchasedBoots,
        int? itemId,
        int timestampMs,
        IReadOnlySet<int> starterItemIds,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
    {
        if (itemId is not > 0 || starterItemIds.Contains(itemId.Value))
        {
            return;
        }

        if (!itemMetadataById.TryGetValue(itemId.Value, out var metadata)
            || metadata is not { InStore: true, IsConsumable: false, IsBootsItem: true })
        {
            return;
        }

        purchasedBoots.Add(new CandidateBootPurchase(metadata, timestampMs));
    }

    private static void RemoveBootPurchaseCandidate(
        List<CandidateBootPurchase> purchasedBoots,
        int? itemId)
    {
        if (itemId is not > 0)
        {
            return;
        }

        for (var index = purchasedBoots.Count - 1; index >= 0; index--)
        {
            if (purchasedBoots[index].Metadata.Id != itemId.Value)
            {
                continue;
            }

            purchasedBoots.RemoveAt(index);
            return;
        }
    }

    private sealed record CandidateBootPurchase(ItemMetadata Metadata, int TimestampMs);
}
