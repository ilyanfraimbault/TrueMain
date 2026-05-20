using Core.Lol.Items;
using Data.Entities;

namespace Ingestor.Processes.Components.PatternAggregation;

public static class FinalBuildResolver
{
    private const int MaxBuildItems = 7;
    private const string UtilityPosition = "UTILITY";

    private static readonly IReadOnlySet<int> IgnoredFinalBuildItemIds =
        new HashSet<int>(LolItemIds.Trinkets.All) { LolItemIds.Cull };

    // Position-aware wrapper around `Resolve`. For UTILITY participants, the
    // support quest completion (Bloodsong, Solstice Sleigh, …) is pinned at
    // BuildItem0 — the player has held that inventory slot since minute 0,
    // it's not a build decision that fits anywhere on the timeline. The
    // completion ID is preserved (not collapsed to the starter), so the UI
    // can still distinguish "this support runs Bloodsong vs. Solstice
    // Sleigh". Non-UTILITY positions get the timestamp-ordered build
    // untouched.
    public static int[] ResolveOrdered(
        IReadOnlyList<ItemEvent> itemEvents,
        IReadOnlyList<int> finalItems,
        IReadOnlyCollection<int> starterItems,
        string? position,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
    {
        var buildItems = Resolve(itemEvents, finalItems, starterItems, itemMetadataById);

        if (buildItems.Length == 0 || !IsUtilityPosition(position))
        {
            return buildItems;
        }

        var completionIndex = FindFirstSupportQuestCompletion(buildItems, itemMetadataById);
        if (completionIndex <= 0)
        {
            return buildItems;
        }

        var reordered = new int[buildItems.Length];
        reordered[0] = buildItems[completionIndex];
        Array.Copy(buildItems, 0, reordered, 1, completionIndex);
        Array.Copy(buildItems, completionIndex + 1, reordered, completionIndex + 1, buildItems.Length - completionIndex - 1);
        return reordered;
    }

    private static bool IsUtilityPosition(string? position)
        => string.Equals(position, UtilityPosition, StringComparison.OrdinalIgnoreCase);

    private static int FindFirstSupportQuestCompletion(
        int[] buildItems,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
    {
        for (var index = 0; index < buildItems.Length; index++)
        {
            if (itemMetadataById.TryGetValue(buildItems[index], out var metadata)
                && metadata.IsSupportQuestCompletion)
            {
                return index;
            }
        }

        return -1;
    }

    public static int[] Resolve(
        IReadOnlyList<ItemEvent> itemEvents,
        IReadOnlyList<int> finalItems,
        IReadOnlyCollection<int> starterItems,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
    {
        var starterItemIds = starterItems
            .Where(itemId => itemId > 0)
            .ToHashSet();

        var finalInventory = finalItems
            .Where(itemId => itemId > 0)
            .Where(itemId => !starterItemIds.Contains(itemId))
            .Distinct()
            .Select(itemId => itemMetadataById.TryGetValue(itemId, out var metadata) ? metadata : null)
            .Where(metadata => metadata is not null)
            .Cast<ItemMetadata>()
            .Where(IsEligibleFinalBuildItem)
            .ToDictionary(metadata => metadata.Id, metadata => metadata);

        if (finalInventory.Count == 0)
        {
            return [];
        }

        var completionTimes = BuildFinalItemCompletionTimes(itemEvents, finalInventory);

        return finalInventory.Values
            .OrderBy(metadata => completionTimes.GetValueOrDefault(metadata.Id, int.MaxValue))
            .ThenBy(metadata => metadata.Id)
            .Take(MaxBuildItems)
            .Select(GetDisplayedBuildItemId)
            .ToArray();
    }

    private static Dictionary<int, int> BuildFinalItemCompletionTimes(
        IReadOnlyList<ItemEvent> itemEvents,
        IReadOnlyDictionary<int, ItemMetadata> finalInventory)
    {
        var trackedItemIds = finalInventory.Keys.ToHashSet();
        var completionTimes = new Dictionary<int, int>();
        var transformSourceCompletionTimes = new Dictionary<int, int>();

        foreach (var itemEvent in itemEvents.OrderBy(itemEvent => itemEvent.TimestampMs))
        {
            switch (itemEvent.EventType.ToUpperInvariant())
            {
                case "ITEM_PURCHASED":
                    AddTrackedItem(itemEvent.ItemId, itemEvent.TimestampMs, trackedItemIds, completionTimes);
                    AddTransformSourceCompletionTime(itemEvent.ItemId, itemEvent.TimestampMs, finalInventory, transformSourceCompletionTimes);
                    break;
                case "ITEM_SOLD":
                case "ITEM_DESTROYED":
                    RemoveTrackedItem(itemEvent.ItemId, trackedItemIds, completionTimes);
                    break;
                case "ITEM_UNDO":
                    RemoveTrackedItem(itemEvent.BeforeId ?? itemEvent.ItemId, trackedItemIds, completionTimes);
                    AddTrackedItem(itemEvent.AfterId, itemEvent.TimestampMs, trackedItemIds, completionTimes);
                    AddTransformSourceCompletionTime(itemEvent.AfterId, itemEvent.TimestampMs, finalInventory, transformSourceCompletionTimes);
                    break;
            }
        }

        foreach (var metadata in finalInventory.Values)
        {
            if (!metadata.IsInventoryTransformItem
                || completionTimes.ContainsKey(metadata.Id)
                || metadata.TransformFromItemId is not > 0
                || !transformSourceCompletionTimes.TryGetValue(metadata.TransformFromItemId.Value, out var completionTime))
            {
                continue;
            }

            completionTimes[metadata.Id] = completionTime;
        }

        return completionTimes;
    }

    private static void AddTrackedItem(
        int? itemId,
        int timestampMs,
        IReadOnlySet<int> trackedItemIds,
        IDictionary<int, int> completionTimes)
    {
        if (itemId is not > 0 || !trackedItemIds.Contains(itemId.Value))
        {
            return;
        }

        completionTimes[itemId.Value] = timestampMs;
    }

    private static void RemoveTrackedItem(
        int? itemId,
        IReadOnlySet<int> trackedItemIds,
        IDictionary<int, int> completionTimes)
    {
        if (itemId is not > 0 || !trackedItemIds.Contains(itemId.Value))
        {
            return;
        }

        completionTimes.Remove(itemId.Value);
    }

    private static void AddTransformSourceCompletionTime(
        int? itemId,
        int timestampMs,
        IReadOnlyDictionary<int, ItemMetadata> finalInventory,
        IDictionary<int, int> transformSourceCompletionTimes)
    {
        if (itemId is not > 0)
        {
            return;
        }

        foreach (var metadata in finalInventory.Values)
        {
            if (!metadata.IsInventoryTransformItem || metadata.TransformFromItemId != itemId.Value)
            {
                continue;
            }

            transformSourceCompletionTimes[itemId.Value] = timestampMs;
        }
    }

    private static bool IsEligibleFinalBuildItem(ItemMetadata metadata)
        => metadata is { IsFinalItem: true, IsConsumable: false }
           && (metadata.InStore || metadata.IsInventoryTransformItem)
           && !IgnoredFinalBuildItemIds.Contains(metadata.Id)
           && !metadata.IsBootsItem;

    private static int GetDisplayedBuildItemId(ItemMetadata metadata)
        => metadata.IsInventoryTransformItem && metadata.TransformFromItemId is > 0
            ? metadata.TransformFromItemId.Value
            : metadata.Id;
}
