using Data.Entities;

namespace Data.BuildFacts;

/// <summary>One slot of a recipe and where the game bought it among the item's components.</summary>
/// <param name="ComponentId">The component the recipe lists.</param>
/// <param name="Occurrence">0 for the first time the recipe lists it, 1 for a second copy (two Long Swords).</param>
/// <param name="Rank">1 for the component the game started the item with; the components never bought on their own share the rank after the last one that was.</param>
public readonly record struct ComponentRank(int ComponentId, int Occurrence, int Rank);

/// <summary>The order one game bought the components of one item in.</summary>
public sealed record ComponentOrder(int ItemId, IReadOnlyList<ComponentRank> Ranks);

/// <summary>
/// The order a game bought an item's components in (#2183): for each item it completed, which
/// of the recipe's components it started with, which came second, and so on — the order a
/// player walks to the shop with, which the recipe alone does not say.
/// </summary>
/// <remarks>
/// <para>
/// <b>The timeline is replayed as an inventory.</b> Each purchase becomes a token holding the
/// tokens it consumed, so a completed item carries the very purchases that went into it and
/// nothing bought for another item can be counted for it. A combine shows on the timeline as
/// the components' <c>ITEM_DESTROYED</c> at the purchase's own timestamp, just before it; those
/// are what the purchase claims first, then whatever of its recipe the inventory still holds —
/// the shop always spends a held component. <c>ITEM_UNDO</c> takes the purchase back and
/// returns what it had consumed; a sale undone returns the sold token.
/// </para>
/// <para>
/// <b>A component counts from its first purchase.</b> A sub-component bought towards an
/// intermediate of the recipe (an Amplifying Tome towards Lost Chapter) is that intermediate
/// started, so the intermediate ranks from the Tome. A component never bought on its own —
/// paid for with the completion itself — ranks after every component that was, all such
/// tied. A game that bought the item outright says nothing about the order and is left out.
/// </para>
/// <para>
/// <b>Intermediates are measured too</b>, from the same tokens: Lost Chapter's own components
/// in the order this game bought them, once per game, so the panel can say which part of it
/// to buy first when the gold does not cover it whole.
/// </para>
/// </remarks>
public static class ComponentOrderResolver
{
    /// <summary>Deeper than any recipe the shop has; bounds the walk if metadata ever loops.</summary>
    private const int MaxDepth = 4;

    public static IReadOnlyList<ComponentOrder> Resolve(
        IReadOnlyList<ItemEvent> itemEvents,
        IReadOnlyCollection<int> completedItems,
        IReadOnlyDictionary<int, ItemMetadata> metadata)
    {
        ArgumentNullException.ThrowIfNull(itemEvents);
        ArgumentNullException.ThrowIfNull(completedItems);
        ArgumentNullException.ThrowIfNull(metadata);

        var wanted = completedItems.Where(id => id > 0 && Recipe(id, metadata).Count > 0).ToHashSet();
        if (wanted.Count == 0)
        {
            return [];
        }

        var completions = Replay(itemEvents, wanted, metadata);

        var orders = new List<ComponentOrder>();
        var observed = new HashSet<int>();
        foreach (var itemId in completedItems.Distinct())
        {
            if (completions.TryGetValue(itemId, out var tokens) && tokens.Count > 0)
            {
                Observe(tokens[0], metadata, observed, orders, depth: 0);
            }
        }

        return orders;
    }

    /// <summary>The tokens of every wanted item the replay created and no undo took back, oldest first.</summary>
    private static Dictionary<int, List<Token>> Replay(
        IReadOnlyList<ItemEvent> itemEvents,
        IReadOnlySet<int> wanted,
        IReadOnlyDictionary<int, ItemMetadata> metadata)
    {
        var inventory = new List<Token>();
        var sold = new List<Token>();
        var completions = wanted.ToDictionary(id => id, _ => new List<Token>());

        // The tokens destroyed at the current timestamp, waiting for the purchase that consumed
        // them; and the ones a purchase took from the inventory at that timestamp, so a destroy
        // the timeline lists after it does not take a second copy.
        var destroyed = new List<Token>();
        var consumed = new List<Token>();
        var at = int.MinValue;

        foreach (var itemEvent in itemEvents.OrderBy(itemEvent => itemEvent.TimestampMs))
        {
            if (itemEvent.TimestampMs != at)
            {
                at = itemEvent.TimestampMs;
                destroyed.Clear();
                consumed.Clear();
            }

            switch (ItemEventTypes.Classify(itemEvent.EventType))
            {
                case ItemEventKind.Purchased when itemEvent.ItemId > 0:
                    var parts = TakeParts(itemEvent.ItemId, inventory, destroyed, consumed, metadata, depth: 0);
                    var token = new Token(itemEvent.ItemId, itemEvent.TimestampMs, parts);
                    inventory.Add(token);
                    if (completions.TryGetValue(itemEvent.ItemId, out var list))
                    {
                        list.Add(token);
                    }

                    break;
                case ItemEventKind.Destroyed:
                    if (Take(consumed, itemEvent.ItemId) is null && Take(inventory, itemEvent.ItemId) is { } gone)
                    {
                        destroyed.Add(gone);
                    }

                    break;
                case ItemEventKind.Sold:
                    if (TakeNewest(inventory, itemEvent.ItemId) is { } sale)
                    {
                        sold.Add(sale);
                    }

                    break;
                case ItemEventKind.Undo:
                    Undo(itemEvent, inventory, sold, completions);
                    break;
            }
        }

        return completions;
    }

    private static void Undo(
        ItemEvent itemEvent,
        List<Token> inventory,
        List<Token> sold,
        Dictionary<int, List<Token>> completions)
    {
        var before = itemEvent.BeforeId ?? itemEvent.ItemId;
        if (before > 0 && TakeNewest(inventory, before) is { } undone)
        {
            inventory.AddRange(undone.Parts);
            if (completions.TryGetValue(before, out var list))
            {
                list.Remove(undone);
            }
        }

        if (itemEvent.AfterId is int after && after > 0)
        {
            inventory.Add(TakeNewest(sold, after) ?? new Token(after, itemEvent.TimestampMs, []));
        }
    }

    /// <summary>
    /// What a purchase of <paramref name="itemId"/> consumed: for each component of its recipe,
    /// the copy destroyed at this timestamp, else one still held, else — for a component with a
    /// recipe of its own — whatever of <em>its</em> components was, as that component started.
    /// </summary>
    private static List<Token> TakeParts(
        int itemId,
        List<Token> inventory,
        List<Token> destroyed,
        List<Token> consumed,
        IReadOnlyDictionary<int, ItemMetadata> metadata,
        int depth)
    {
        var parts = new List<Token>();
        if (depth >= MaxDepth)
        {
            return parts;
        }

        foreach (var componentId in Recipe(itemId, metadata))
        {
            var part = Take(destroyed, componentId);
            if (part is null && Take(inventory, componentId) is { } held)
            {
                consumed.Add(held);
                part = held;
            }

            if (part is null)
            {
                var subParts = TakeParts(componentId, inventory, destroyed, consumed, metadata, depth + 1);
                if (subParts.Count > 0)
                {
                    part = new Token(componentId, subParts.Min(sub => sub.FirstTouchMs), subParts);
                }
            }

            if (part is not null)
            {
                parts.Add(part);
            }
        }

        return parts;
    }

    /// <summary>Ranks the recipe slots of one completed token, then of its intermediates.</summary>
    private static void Observe(
        Token token,
        IReadOnlyDictionary<int, ItemMetadata> metadata,
        HashSet<int> observed,
        List<ComponentOrder> orders,
        int depth)
    {
        if (depth >= MaxDepth || token.Parts.Count == 0 || !observed.Add(token.ItemId))
        {
            return;
        }

        var recipe = Recipe(token.ItemId, metadata);
        var slots = new List<(int ComponentId, int Occurrence, Token? Part)>(recipe.Count);
        foreach (var componentId in recipe)
        {
            var occurrence = slots.Count(slot => slot.ComponentId == componentId);
            var part = token.Parts
                .Where(candidate => candidate.ItemId == componentId)
                .OrderBy(candidate => candidate.FirstTouchMs)
                .ElementAtOrDefault(occurrence);
            slots.Add((componentId, occurrence, part));
        }

        var touches = slots
            .Where(slot => slot.Part is not null)
            .Select(slot => slot.Part!.FirstTouchMs)
            .Order()
            .ToList();
        if (touches.Count == 0)
        {
            return;
        }

        // Competition ranking: two components bought in the same instant share a rank, and the
        // ones never bought on their own all come after the last that was.
        var ranks = slots
            .Select(slot => new ComponentRank(
                slot.ComponentId,
                slot.Occurrence,
                slot.Part is { } part ? touches.IndexOf(part.FirstTouchMs) + 1 : touches.Count + 1))
            .ToList();
        orders.Add(new ComponentOrder(token.ItemId, ranks));

        foreach (var (_, _, part) in slots)
        {
            if (part is not null && Recipe(part.ItemId, metadata).Count > 0)
            {
                Observe(part, metadata, observed, orders, depth + 1);
            }
        }
    }

    private static IReadOnlyList<int> Recipe(int itemId, IReadOnlyDictionary<int, ItemMetadata> metadata)
        => metadata.TryGetValue(itemId, out var item) ? item.Components : [];

    private static Token? Take(List<Token> tokens, int itemId)
    {
        var index = tokens.FindIndex(token => token.ItemId == itemId);
        if (index < 0)
        {
            return null;
        }

        var token = tokens[index];
        tokens.RemoveAt(index);
        return token;
    }

    private static Token? TakeNewest(List<Token> tokens, int itemId)
    {
        var index = tokens.FindLastIndex(token => token.ItemId == itemId);
        if (index < 0)
        {
            return null;
        }

        var token = tokens[index];
        tokens.RemoveAt(index);
        return token;
    }

    /// <summary>An item in the replayed inventory, and the tokens its purchase consumed.</summary>
    private sealed class Token(int itemId, int purchasedAtMs, List<Token> parts)
    {
        public int ItemId { get; } = itemId;

        public List<Token> Parts { get; } = parts;

        /// <summary>When the first purchase that went into it was made: its own, or its earliest part's.</summary>
        public int FirstTouchMs { get; } = parts.Count == 0 ? purchasedAtMs : Math.Min(purchasedAtMs, parts.Min(part => part.FirstTouchMs));
    }
}
