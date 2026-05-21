using Core.Lol.Items;
using Data.Entities;

namespace Ingestor.Processes.Components.PatternAggregation;

public static class StarterItemAnalyzer
{
    private const int StarterPurchaseWindowMs = 120_000;
    private const int StarterBatchGapMs = 15_000;
    private const int StarterMaxTotalCost = 500;

    public static List<int> BuildStarterItems(
        IReadOnlyList<ItemEvent> itemEvents,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
        => Analyze(itemEvents, itemMetadataById).Items;

    public static StarterItemsAnalysis Analyze(
        IReadOnlyList<ItemEvent> itemEvents,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
    {
        var orderedEvents = itemEvents
            .OrderBy(itemEvent => itemEvent.TimestampMs)
            .ToArray();

        var earlyEvents = ExtractStarterBatchEvents(orderedEvents);

        if (earlyEvents.Length == 0)
        {
            return new StarterItemsAnalysis([], "NoEarlyEvents", 0, earlyEvents);
        }

        var starterItems = new List<int>();
        var ignoredOverflowPurchases = new List<int>();

        foreach (var itemEvent in earlyEvents)
        {
            switch (itemEvent.EventType.ToUpperInvariant())
            {
                case "ITEM_PURCHASED":
                    TryAddStarterItem(starterItems, itemEvent.ItemId, itemMetadataById, ignoredOverflowPurchases);
                    break;
                case "ITEM_SOLD":
                    RemoveStarterItem(starterItems, itemEvent.ItemId);
                    break;
                case "ITEM_UNDO":
                    RemoveStarterItem(starterItems, itemEvent.BeforeId ?? itemEvent.ItemId);
                    TryAddStarterItem(starterItems, itemEvent.AfterId, itemMetadataById, ignoredOverflowPurchases);
                    break;
            }
        }

        TryInferImplicitSupportStarterItem(starterItems, orderedEvents, itemMetadataById);

        if (starterItems.Count == 0)
        {
            return new StarterItemsAnalysis([], "EmptyBasketAfterEarlyEvents", 0, earlyEvents);
        }

        SortStarterItemsCanonically(starterItems, itemMetadataById);

        var totalCost = 0;
        foreach (var itemId in starterItems)
        {
            if (!ShouldCountTowardStarterBudget(itemId, itemMetadataById))
            {
                continue;
            }

            if (!itemMetadataById.TryGetValue(itemId, out var metadata) || metadata.PriceTotal <= 0)
            {
                return new StarterItemsAnalysis([], $"MissingOrInvalidMetadata:{itemId}", 0, earlyEvents);
            }

            totalCost += metadata.PriceTotal;
        }

        var reason = ignoredOverflowPurchases.Count > 0
            ? $"DetectedIgnoringOverflow:{string.Join(",", ignoredOverflowPurchases)}"
            : "Detected";
        return new StarterItemsAnalysis(starterItems, reason, totalCost, earlyEvents);
    }

    // Canonical order = most-expensive first, ties broken by item id ascending.
    // Makes the StarterItemsKey order-independent so the dim table stores one
    // row per item set (not one per purchase sequence), and matches how the UI
    // expects to display starters (Doran's first, then potions).
    private static void SortStarterItemsCanonically(
        List<int> starterItems,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
    {
        starterItems.Sort((left, right) =>
        {
            var leftPrice = itemMetadataById.TryGetValue(left, out var leftMeta) ? leftMeta.PriceTotal : 0;
            var rightPrice = itemMetadataById.TryGetValue(right, out var rightMeta) ? rightMeta.PriceTotal : 0;
            var byPrice = rightPrice.CompareTo(leftPrice);
            return byPrice != 0 ? byPrice : left.CompareTo(right);
        });
    }

    private static void TryAddStarterItem(
        ICollection<int> starterItems,
        int? itemId,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadataById,
        ICollection<int> ignoredOverflowPurchases)
    {
        if (itemId is not > 0 || ShouldIgnoreStarterItem(itemId.Value))
        {
            return;
        }

        if (!itemMetadataById.TryGetValue(itemId.Value, out var metadata) || metadata.PriceTotal <= 0)
        {
            starterItems.Add(itemId.Value);
            return;
        }

        var currentTotal = 0;
        foreach (var existingItemId in starterItems)
        {
            if (!ShouldCountTowardStarterBudget(existingItemId, itemMetadataById))
            {
                continue;
            }

            if (!itemMetadataById.TryGetValue(existingItemId, out var existingMetadata) || existingMetadata.PriceTotal <= 0)
            {
                starterItems.Add(itemId.Value);
                return;
            }

            currentTotal += existingMetadata.PriceTotal;
        }

        if (currentTotal + metadata.PriceTotal > StarterMaxTotalCost)
        {
            ignoredOverflowPurchases.Add(itemId.Value);
            return;
        }

        starterItems.Add(itemId.Value);
    }

    private static void RemoveStarterItem(List<int> starterItems, int? itemId)
    {
        if (itemId is not > 0)
        {
            return;
        }

        for (var index = starterItems.Count - 1; index >= 0; index--)
        {
            if (starterItems[index] != itemId.Value)
            {
                continue;
            }

            starterItems.RemoveAt(index);
            return;
        }
    }

    private static void TryInferImplicitSupportStarterItem(
        List<int> starterItems,
        IReadOnlyList<ItemEvent> orderedEvents,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
    {
        // Already have a support-quest item in the starter list (either the
        // base root or one of the completions) — nothing to infer.
        if (starterItems.Any(itemId => IsSupportQuestFamilyMember(itemId, itemMetadataById)))
        {
            return;
        }

        // Walk the events: if we see a completion in the player's final
        // inventory (or via any transform/undo), surface that completion as
        // the starter — it's what the player actually had in the support
        // slot at the end of the match. Otherwise, if any family member
        // (root/intermediate/completion) is referenced, fall back to the
        // patch's root starter so the player's lane intent is preserved.
        int? observedCompletion = null;
        int? observedAnyFamilyMember = null;

        foreach (var itemEvent in orderedEvents)
        {
            foreach (var candidate in EnumerateRelevantItemIds(itemEvent))
            {
                if (!itemMetadataById.TryGetValue(candidate, out var metadata))
                {
                    continue;
                }
                if (metadata.IsSupportQuestCompletion)
                {
                    observedCompletion ??= candidate;
                }
                if (metadata.IsSupportQuestStarter
                    || metadata.IsSupportQuestIntermediate
                    || metadata.IsSupportQuestCompletion)
                {
                    observedAnyFamilyMember ??= candidate;
                }
            }
        }

        if (observedCompletion is > 0)
        {
            TryAddStarterItemIgnoringBudget(starterItems, observedCompletion.Value);
            return;
        }

        if (observedAnyFamilyMember is > 0)
        {
            var rootId = ResolveSupportQuestRoot(itemMetadataById);
            if (rootId > 0)
            {
                TryAddStarterItemIgnoringBudget(starterItems, rootId);
            }
        }
    }

    private static IEnumerable<int> EnumerateRelevantItemIds(ItemEvent itemEvent)
    {
        if (itemEvent.ItemId > 0)
        {
            yield return itemEvent.ItemId;
        }
        if (itemEvent.BeforeId is > 0)
        {
            yield return itemEvent.BeforeId.Value;
        }
        if (itemEvent.AfterId is > 0)
        {
            yield return itemEvent.AfterId.Value;
        }
    }

    private static int ResolveSupportQuestRoot(IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
    {
        foreach (var (id, metadata) in itemMetadataById)
        {
            if (metadata.IsSupportQuestStarter)
            {
                return id;
            }
        }
        return 0;
    }

    private static bool IsSupportQuestFamilyMember(
        int itemId,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
    {
        if (!itemMetadataById.TryGetValue(itemId, out var metadata))
        {
            return false;
        }
        return metadata.IsSupportQuestStarter
            || metadata.IsSupportQuestIntermediate
            || metadata.IsSupportQuestCompletion;
    }

    private static void TryAddStarterItemIgnoringBudget(
        ICollection<int> starterItems,
        int? itemId)
    {
        if (itemId is not > 0 || ShouldIgnoreStarterItem(itemId.Value))
        {
            return;
        }

        starterItems.Add(itemId.Value);
    }

    private static ItemEvent[] ExtractStarterBatchEvents(IReadOnlyList<ItemEvent> orderedEvents)
    {
        var batch = new List<ItemEvent>();
        int? previousTimestampMs = null;

        foreach (var itemEvent in orderedEvents)
        {
            if (itemEvent.TimestampMs > StarterPurchaseWindowMs)
            {
                break;
            }

            if (batch.Count == 0)
            {
                batch.Add(itemEvent);
                previousTimestampMs = itemEvent.TimestampMs;
                continue;
            }

            if (previousTimestampMs.HasValue && itemEvent.TimestampMs - previousTimestampMs.Value > StarterBatchGapMs)
            {
                break;
            }

            batch.Add(itemEvent);
            previousTimestampMs = itemEvent.TimestampMs;
        }

        return batch.ToArray();
    }

    internal static bool ShouldIgnoreStarterItem(int itemId)
        => LolItemIds.Trinkets.All.Contains(itemId);

    /// <summary>
    /// Items that should not count toward the 500g starter budget. Trinkets
    /// are free; support-quest family members (root, intermediates,
    /// completions) are technically held from minute 0 so we don't want a
    /// completion's late-game price to blow past the budget and silently
    /// drop legitimate starter items.
    /// </summary>
    internal static bool ShouldCountTowardStarterBudget(
        int itemId,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
    {
        if (ShouldIgnoreStarterItem(itemId))
        {
            return false;
        }
        if (!itemMetadataById.TryGetValue(itemId, out var metadata))
        {
            return true;
        }
        return !metadata.IsSupportQuestStarter
            && !metadata.IsSupportQuestIntermediate
            && !metadata.IsSupportQuestCompletion;
    }
}

public sealed record StarterItemsAnalysis(
    List<int> Items,
    string Reason,
    int TotalCost,
    IReadOnlyList<ItemEvent> EarlyEvents);
