using Data.Entities;

namespace Ingestor.Processes.Components.PatternAggregation;

internal static class ChampionPatternNormalization
{
    private static readonly HashSet<int> IgnoredStarterItemIds =
    [
        3340,
        3363,
        3364,
        3330
    ];

    private static readonly HashSet<int> IgnoredFinalBuildItemIds =
    [
        3340,
        3363,
        3364,
        3330,
        1083
    ];

    private static readonly HashSet<int> SupportStarterQuestItemIds =
    [
        3865,
        3866,
        3867
    ];

    private static readonly HashSet<string> ValidTeamPositions = new(StringComparer.OrdinalIgnoreCase)
    {
        "TOP",
        "JUNGLE",
        "MIDDLE",
        "BOTTOM",
        "UTILITY"
    };

    private const int StarterPurchaseWindowMs = 120_000;
    private const int StarterBatchGapMs = 15_000;
    private const int StarterMaxTotalCost = 500;
    private const int MaxBuildItems = 7;

    public static string NormalizePatchVersion(string gameVersion)
    {
        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            return string.Empty;
        }

        var segments = gameVersion.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length >= 2 ? $"{segments[0]}.{segments[1]}" : gameVersion;
    }

    public static (int spell1Id, int spell2Id) NormalizeSummonerPair(int summoner1Id, int summoner2Id)
        => summoner1Id <= summoner2Id ? (summoner1Id, summoner2Id) : (summoner2Id, summoner1Id);

    public static string BuildSkillOrderKey(IEnumerable<SkillEvent> skillEvents)
    {
        var basicSkillStates = new Dictionary<int, (int Rank, int LastRankUpAtMs)>
        {
            [1] = (0, int.MaxValue),
            [2] = (0, int.MaxValue),
            [3] = (0, int.MaxValue)
        };
        var sequence = new List<int>(3);

        foreach (var skill in skillEvents
                     .Where(skill => skill.LevelUpType.Equals("NORMAL", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(skill => skill.TimestampMs))
        {
            if (!basicSkillStates.TryGetValue(skill.SkillSlot, out var state))
            {
                continue;
            }

            var updatedRank = state.Rank + 1;
            basicSkillStates[skill.SkillSlot] = (updatedRank, skill.TimestampMs);

            if (updatedRank == 2)
            {
                sequence.Add(skill.SkillSlot);
            }
        }

        if (basicSkillStates.Values.All(state => state.Rank == 0))
        {
            return string.Empty;
        }

        var remainingSlots = basicSkillStates.Keys
            .Except(sequence)
            .OrderByDescending(slot => basicSkillStates[slot].Rank)
            .ThenBy(slot => basicSkillStates[slot].LastRankUpAtMs)
            .ThenBy(slot => slot);

        return string.Join("-", sequence
            .Concat(remainingSlots)
            .Select(slot => slot switch
            {
                1 => "Q",
                2 => "W",
                3 => "E",
                _ => slot.ToString()
            }));
    }

    public static string? NormalizeTeamPosition(string? teamPosition)
    {
        if (string.IsNullOrWhiteSpace(teamPosition))
        {
            return null;
        }

        var normalized = teamPosition.Trim().ToUpperInvariant();
        return ValidTeamPositions.Contains(normalized) ? normalized : null;
    }

    public static List<int> BuildStarterItems(
        IReadOnlyList<ItemEvent> itemEvents,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
        => AnalyzeStarterItems(itemEvents, itemMetadataById).Items;

    public static StarterItemsAnalysis AnalyzeStarterItems(
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

        var totalCost = 0;
        foreach (var itemId in starterItems)
        {
            if (!ShouldCountTowardStarterBudget(itemId))
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

    public static int[] BuildOrderedFinalBuild(
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

    public static int BuildCorrelatedBootsItem(
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
            if (!ShouldCountTowardStarterBudget(existingItemId))
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
        if (starterItems.Any(SupportStarterQuestItemIds.Contains))
        {
            return;
        }

        var referencesSupportQuestItem = orderedEvents.Any(itemEvent =>
            SupportStarterQuestItemIds.Contains(itemEvent.ItemId)
            || (itemEvent.BeforeId is > 0 && SupportStarterQuestItemIds.Contains(itemEvent.BeforeId.Value))
            || (itemEvent.AfterId is > 0 && SupportStarterQuestItemIds.Contains(itemEvent.AfterId.Value)));

        if (!referencesSupportQuestItem)
        {
            return;
        }

        TryAddStarterItemIgnoringBudget(starterItems, 3865, itemMetadataById);
    }

    private static void TryAddStarterItemIgnoringBudget(
        ICollection<int> starterItems,
        int? itemId,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
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

        starterItems.Add(itemId.Value);
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

    private static bool ShouldIgnoreStarterItem(int itemId)
        => IgnoredStarterItemIds.Contains(itemId);

    private static bool ShouldCountTowardStarterBudget(int itemId)
        => !ShouldIgnoreStarterItem(itemId) && !SupportStarterQuestItemIds.Contains(itemId);
}

internal sealed record StarterItemsAnalysis(
    List<int> Items,
    string Reason,
    int TotalCost,
    IReadOnlyList<ItemEvent> EarlyEvents);

internal sealed record CandidateBootPurchase(
    ItemMetadata Metadata,
    int TimestampMs);
