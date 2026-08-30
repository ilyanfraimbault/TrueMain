using Core.Lol.Items;
using Data.Entities;

namespace Data.BuildFacts;

public static class StarterItemAnalyzer
{
    private const int StarterPurchaseWindowMs = 120_000;
    private const int StarterBatchGapMs = 15_000;
    private const int StarterMaxTotalCost = 500;

    public static List<int> BuildStarterItems(
        IReadOnlyList<ItemEvent> itemEvents,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
        => Analyze(itemEvents, [], itemMetadataById).Items;

    public static List<int> BuildStarterItems(
        IReadOnlyList<ItemEvent> itemEvents,
        IReadOnlyList<int> finalItems,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
        => Analyze(itemEvents, finalItems, itemMetadataById).Items;

    /// <summary>
    /// Same fold as <see cref="BuildStarterItems(IReadOnlyList{ItemEvent}, IReadOnlyDictionary{int, ItemMetadata})"/>
    /// with no final inventory to disambiguate a support quest — the shape the unit
    /// tests use when they assert on the budget rather than on the basket.
    /// </summary>
    public static StarterItemsAnalysis Analyze(
        IReadOnlyList<ItemEvent> itemEvents,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
        => Analyze(itemEvents, [], itemMetadataById);

    /// <summary>
    /// Resolves the starter basket <em>and</em> the reasoning around it: the reason code
    /// for an empty or rejected basket, and the paid total the 500g budget is checked
    /// against. This is the entry point every caller that also needs the basket for a
    /// downstream resolver uses — <c>ParticipantBuildFactsLoader</c>,
    /// <c>ChampionPatternAggregateBuilder</c> and <c>ChampionPowerspikeAggregationProcess</c>
    /// all call it and feed <see cref="StarterItemsAnalysis.Items"/> straight into
    /// <see cref="FinalBuildResolver"/> and <see cref="BootsResolver"/>, which must be
    /// told which items were starters or they count them as build slots.
    /// <see cref="BuildStarterItems(IReadOnlyList{ItemEvent}, IReadOnlyList{int}, IReadOnlyDictionary{int, ItemMetadata})"/>
    /// is the shorthand for callers that only want the basket.
    /// </summary>
    public static StarterItemsAnalysis Analyze(
        IReadOnlyList<ItemEvent> itemEvents,
        IReadOnlyList<int> finalItems,
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
            switch (ItemEventTypes.Classify(itemEvent.EventType))
            {
                case ItemEventKind.Purchased:
                    TryAddStarterItem(starterItems, itemEvent.ItemId, itemMetadataById, ignoredOverflowPurchases);
                    break;
                case ItemEventKind.Sold:
                    RemoveStarterItem(starterItems, itemEvent.ItemId);
                    break;
                case ItemEventKind.Undo:
                    RemoveStarterItem(starterItems, itemEvent.BeforeId ?? itemEvent.ItemId);
                    TryAddStarterItem(starterItems, itemEvent.AfterId, itemMetadataById, ignoredOverflowPurchases);
                    break;
            }
        }

        NormalizeSupportQuestStarterItem(starterItems, orderedEvents, finalItems, itemMetadataById);

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

    /// <summary>
    /// Normalize the support-quest family representation in the starter
    /// list. World Atlas is auto-gifted at game start (no
    /// <c>ITEM_PURCHASED</c> event), so the early-events loop above
    /// typically captures only <c>[2003, 2003]</c> for a support player.
    /// The completion (Bloodsong, etc.) is what we want to surface in the
    /// starter slot once the quest finishes.
    ///
    /// Detection cross-references two signals because Riot's timeline is
    /// inconsistent:
    /// - <paramref name="orderedEvents"/> — fires <c>ITEM_PURCHASED</c> for
    ///   the completion in only ~18% of support matches (the quest-choice
    ///   selection isn't always recorded). The intermediates'
    ///   <c>ITEM_DESTROYED</c> events on transformation are reliable.
    /// - <paramref name="finalItems"/> — the player's end-of-game
    ///   inventory contains the chosen completion ~97% of the time when
    ///   the quest finished. This is the authoritative signal.
    ///
    /// We check both, taking the first completion observed in either
    /// source. Intermediates count toward the "lane intent" fallback
    /// (their destruction proves the player was on a support quest) but
    /// never replace the root in the starter slot.
    ///
    /// Rules:
    /// - Completion observed in events or final inventory → strip any
    ///   root/intermediate the early loop captured and surface the
    ///   completion. Riot's chain is single-branch per match, so picking
    ///   the first completion seen is safe.
    /// - No completion, but a family member is already in the starter
    ///   list (root bought at t=0, quest didn't finish) → leave it alone.
    /// - No completion, no family in starter, but family members
    ///   referenced anywhere → surface the patch's root so lane intent
    ///   isn't lost.
    /// - Non-support match: nothing to do.
    /// </summary>
    private static void NormalizeSupportQuestStarterItem(
        List<int> starterItems,
        IReadOnlyList<ItemEvent> orderedEvents,
        IReadOnlyList<int> finalItems,
        IReadOnlyDictionary<int, ItemMetadata> itemMetadataById)
    {
        int? observedCompletion = null;
        var referencesFamily = false;

        foreach (var itemEvent in orderedEvents)
        {
            foreach (var (candidate, provesOwnership) in EnumerateRelevantItemIds(itemEvent))
            {
                if (!itemMetadataById.TryGetValue(candidate, out var metadata))
                {
                    continue;
                }

                if (metadata.IsSupportQuestCompletion)
                {
                    // Only a held completion counts. A destroyed one is somebody else's, and
                    // an undo's before-side was never held — see EnumerateRelevantItemIds.
                    if (!provesOwnership)
                    {
                        continue;
                    }

                    observedCompletion ??= candidate;
                    referencesFamily = true;
                }
                else if (metadata.IsSupportQuestStarter || metadata.IsSupportQuestIntermediate)
                {
                    // A destroyed root or intermediate is the gifted World Atlas
                    // transforming, which is the whole reason this fallback exists.
                    referencesFamily = true;
                }
            }
        }

        foreach (var itemId in finalItems)
        {
            if (itemId <= 0 || !itemMetadataById.TryGetValue(itemId, out var metadata))
            {
                continue;
            }
            if (metadata.IsSupportQuestCompletion)
            {
                observedCompletion ??= itemId;
                referencesFamily = true;
            }
            else if (metadata.IsSupportQuestStarter || metadata.IsSupportQuestIntermediate)
            {
                referencesFamily = true;
            }
        }

        if (observedCompletion is > 0)
        {
            // Quest finished: strip any root/intermediate the early loop kept,
            // then add the completion if it isn't already there.
            for (var i = starterItems.Count - 1; i >= 0; i--)
            {
                if (!itemMetadataById.TryGetValue(starterItems[i], out var metadata))
                {
                    continue;
                }
                if (metadata.IsSupportQuestStarter || metadata.IsSupportQuestIntermediate)
                {
                    starterItems.RemoveAt(i);
                }
            }
            if (!starterItems.Contains(observedCompletion.Value))
            {
                TryAddStarterItemIgnoringBudget(starterItems, observedCompletion.Value);
            }
            return;
        }

        if (starterItems.Any(itemId => IsSupportQuestFamilyMember(itemId, itemMetadataById)))
        {
            return;
        }

        if (!referencesFamily)
        {
            return;
        }

        var rootId = ResolveSupportQuestRoot(itemMetadataById);
        if (rootId > 0)
        {
            TryAddStarterItemIgnoringBudget(starterItems, rootId);
        }
    }

    /// <summary>
    /// Item ids referenced by an event, for the quest-family scan, each paired with whether
    /// <em>that specific candidate</em> proves the participant held it.
    ///
    /// <para>
    /// The distinction only matters for the completions (Celestial Opposition, Dream Maker,
    /// Solstice Sleigh, Bloodsong), and it matters a lot: a completion is worth 400 g and is
    /// injected into the starter basket past its 500 g budget. Nobody destroys their own
    /// completed support item — but on production, junglers' event streams carry six to eight
    /// <c>ITEM_DESTROYED</c> events naming one, and that was enough to make the scan below
    /// conclude the quest was finished. The result was a jungler's starter reading
    /// "Scorchclaw Pup + Bloodsong + Health Potion" — 900 g of items they never bought.
    /// </para>
    ///
    /// <para>
    /// <c>ITEM_PURCHASED</c> proves ownership of <see cref="ItemEvent.ItemId"/>.
    /// <c>ITEM_UNDO</c> proves ownership of <see cref="ItemEvent.AfterId"/> only — that is
    /// what the player is left holding — never of <see cref="ItemEvent.BeforeId"/>, which is
    /// what they are giving back. Treating <c>BeforeId</c> as owned would reopen this exact
    /// bug through undo: a support who buys the wrong completion and immediately undoes it
    /// never held it, the same way a jungler never held one they merely saw destroyed.
    /// A destroyed root or intermediate stays trusted for the family-reference fallback (not
    /// gated on ownership): that is exactly what a support's own World Atlas looks like when
    /// it transforms, and it is the only trace of an item the game gifts without an
    /// <c>ITEM_PURCHASED</c> event.
    /// </para>
    /// </summary>
    private static IEnumerable<(int ItemId, bool ProvesOwnership)> EnumerateRelevantItemIds(ItemEvent itemEvent)
    {
        var kind = ItemEventTypes.Classify(itemEvent.EventType);
        var isPurchase = kind == ItemEventKind.Purchased;
        var isUndo = kind == ItemEventKind.Undo;

        if (itemEvent.ItemId > 0)
        {
            yield return (itemEvent.ItemId, isPurchase);
        }
        if (itemEvent.BeforeId is > 0)
        {
            yield return (itemEvent.BeforeId.Value, false);
        }
        if (itemEvent.AfterId is > 0)
        {
            yield return (itemEvent.AfterId.Value, isUndo);
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

/// <summary>
/// The outcome of <see cref="StarterItemAnalyzer.Analyze(IReadOnlyList{ItemEvent}, IReadOnlyList{int}, IReadOnlyDictionary{int, ItemMetadata})"/>.
/// </summary>
/// <param name="Items">
/// The starter basket in canonical order, empty when none could be resolved. Read by
/// every production caller and passed on to the build and boots resolvers.
/// </param>
/// <param name="Reason">
/// Why the basket looks the way it does: <c>Detected</c>, <c>DetectedIgnoringOverflow:…</c>,
/// <c>NoEarlyEvents</c>, <c>EmptyBasketAfterEarlyEvents</c> or <c>MissingOrInvalidMetadata:…</c>.
/// Nothing branches on it — it exists so that "this game has no starters" can be told
/// apart from "this game's metadata is broken" when a basket is investigated by hand,
/// which is otherwise unanswerable from the stored aggregate alone.
/// </param>
/// <param name="TotalCost">
/// Gold paid for the basket, counting only items that count toward the 500g budget
/// (a gifted support-quest root does not). The invariant the unit tests assert on.
/// </param>
/// <param name="EarlyEvents">
/// The purchase batch the basket was derived from, in timestamp order — the input half
/// of the same by-hand investigation <paramref name="Reason"/> serves.
/// </param>
public sealed record StarterItemsAnalysis(
    List<int> Items,
    string Reason,
    int TotalCost,
    IReadOnlyList<ItemEvent> EarlyEvents);
