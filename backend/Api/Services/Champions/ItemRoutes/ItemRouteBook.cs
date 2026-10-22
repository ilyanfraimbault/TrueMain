using Data.BuildFacts;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions.ItemRoutes;

/// <summary>One counter of the component order, as the read keeps it.</summary>
public readonly record struct ComponentOrderCount(int ItemId, int ComponentId, int Occurrence, int Rank, int Games);

/// <summary>
/// The build paths of one champion at one position (#2183): for each item, its components in
/// the order the mains buy them, read off the component-order counters the item-context fold
/// keeps (<c>champion_item_component_orders</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>The order is the mean rank.</b> Every game an item is counted for ranks every slot of its
/// recipe — a component never bought on its own ranks after the ones that were — so a slot's
/// mean rank over those games is comparable across slots; ties go to the slot bought first
/// more often, then to the recipe. A step's share is how often it was bought at the rank the
/// path puts it at.
/// </para>
/// <para>
/// <b>Each item reaches back on its own.</b> The served patch alone when it holds the floor,
/// otherwise the patches before it are added one at a time until it does, up to the window the
/// book was given: a component order moves far less between patches than a pick rate, and a
/// patch day must not blank every path. Below the floor, or when the recipe changed so a slot
/// was never counted, the path is the recipe's order and says so (<c>Measured = false</c>).
/// </para>
/// </remarks>
public sealed class ItemRouteBook
{
    /// <summary>Games below which an order is the recipe's — the floor the build order's boots step uses (#2101).</summary>
    public const int MinGames = 20;

    /// <summary>Deeper than any recipe the shop has.</summary>
    private const int MaxDepth = 4;

    private readonly IReadOnlyList<IReadOnlyList<ComponentOrderCount>> _patches;
    private readonly IReadOnlyDictionary<int, ItemMetadata> _items;
    private readonly Dictionary<int, Pooled> _pooled = [];

    /// <summary>A book over the counters of a patch window and the served patch's recipes.</summary>
    /// <param name="patches">The counters of each patch of the window, the served patch first.</param>
    /// <param name="items">The served patch's item metadata: recipes and prices.</param>
    public ItemRouteBook(IReadOnlyList<IReadOnlyList<ComponentOrderCount>> patches, IReadOnlyDictionary<int, ItemMetadata> items)
    {
        ArgumentNullException.ThrowIfNull(patches);
        ArgumentNullException.ThrowIfNull(items);

        _patches = patches;
        _items = items;
    }

    public static ItemRouteBook Empty { get; } = new([], new Dictionary<int, ItemMetadata>());

    /// <summary>Games behind <paramref name="itemId"/>'s order, over the patches it reached back to.</summary>
    public int Games(int itemId) => Pool(itemId).Games;

    /// <summary><paramref name="itemId"/>'s components in the order the mains buy them; empty for an item with no recipe.</summary>
    public IReadOnlyList<ItemRouteStepReadModel> Route(int itemId) => Route(itemId, depth: 0);

    /// <summary>The champion page's form: the item, its games and its path.</summary>
    public ChampionItemRouteReadModel ReadModel(int itemId)
    {
        var steps = Route(itemId);
        return new ChampionItemRouteReadModel
        {
            ItemId = itemId,
            Games = Games(itemId),
            Measured = steps.Count > 0 && steps[0].Measured,
            Steps = steps,
        };
    }

    private List<ItemRouteStepReadModel> Route(int itemId, int depth)
    {
        if (depth >= MaxDepth || !_items.TryGetValue(itemId, out var item) || item.Components.Count == 0)
        {
            return [];
        }

        var recipe = item.Components;
        var slots = new List<Slot>(recipe.Count);
        foreach (var componentId in recipe)
        {
            slots.Add(new Slot(slots.Count, componentId, slots.Count(slot => slot.ComponentId == componentId)));
        }

        var pooled = Pool(itemId);
        var measured = pooled.Games >= MinGames
            && slots.All(slot => pooled.Ranks.ContainsKey((slot.ComponentId, slot.Occurrence)));

        var ordered = measured
            ? [.. slots
                .OrderBy(slot => MeanRank(pooled.Ranks[(slot.ComponentId, slot.Occurrence)]))
                .ThenByDescending(slot => Share(pooled.Ranks[(slot.ComponentId, slot.Occurrence)], 1))
                .ThenBy(slot => slot.RecipeIndex)]
            : slots;

        return [.. ordered.Select((slot, index) => new ItemRouteStepReadModel
        {
            ItemId = slot.ComponentId,
            Cost = _items.TryGetValue(slot.ComponentId, out var component) ? component.PriceTotal : 0,
            Rank = index + 1,
            Share = measured ? Share(pooled.Ranks[(slot.ComponentId, slot.Occurrence)], index + 1) : null,
            Measured = measured,
            Components = Route(slot.ComponentId, depth + 1),
        })];
    }

    /// <summary>The item's counters over the fewest patches, from the served one back, that hold the floor.</summary>
    private Pooled Pool(int itemId)
    {
        if (_pooled.TryGetValue(itemId, out var cached))
        {
            return cached;
        }

        var ranks = new Dictionary<(int ComponentId, int Occurrence), Dictionary<int, int>>();
        var games = 0;
        foreach (var patch in _patches)
        {
            foreach (var count in patch.Where(count => count.ItemId == itemId))
            {
                var byRank = ranks.TryGetValue((count.ComponentId, count.Occurrence), out var existing)
                    ? existing
                    : ranks[(count.ComponentId, count.Occurrence)] = [];
                byRank[count.Rank] = byRank.GetValueOrDefault(count.Rank) + count.Games;
            }

            // Every counted game ranks every slot, so any slot's total is the item's games; the
            // largest one is, should the recipe have changed inside the window.
            games = ranks.Count == 0 ? 0 : ranks.Values.Max(byRank => byRank.Values.Sum());
            if (games >= MinGames)
            {
                break;
            }
        }

        var pooled = new Pooled(games, ranks);
        _pooled[itemId] = pooled;
        return pooled;
    }

    private static double MeanRank(Dictionary<int, int> byRank)
    {
        var total = byRank.Values.Sum();
        return total == 0 ? double.MaxValue : byRank.Sum(entry => entry.Key * (double)entry.Value) / total;
    }

    private static double Share(Dictionary<int, int> byRank, int rank)
    {
        var total = byRank.Values.Sum();
        return total == 0 ? 0 : byRank.GetValueOrDefault(rank) / (double)total;
    }

    private readonly record struct Slot(int RecipeIndex, int ComponentId, int Occurrence);

    private sealed record Pooled(int Games, Dictionary<(int ComponentId, int Occurrence), Dictionary<int, int>> Ranks);
}
