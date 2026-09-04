using Data.ItemContext;

namespace Ingestor.Processes.Components.ItemContextAggregation;

/// <summary>One champion's slice on one patch — the grain a verdict is rebuilt for.</summary>
public readonly record struct ItemContextScope(int ChampionId, string Position, string Patch);

/// <summary>The grain of a <c>champion_item_context_stats</c> row.</summary>
public readonly record struct ItemContextStatKey(
    int ChampionId,
    string Position,
    string Patch,
    ItemContextSlot Slot,
    int ItemId,
    ItemContextAxis Axis,
    ItemContextBucket Bucket);

/// <summary>The grain of a <c>champion_item_context_totals</c> row.</summary>
public readonly record struct ItemContextTotalKey(
    int ChampionId,
    string Position,
    string Patch,
    ItemContextSlot Slot,
    ItemContextAxis Axis,
    ItemContextBucket Bucket);

/// <summary>Games and wins, accumulated in place.</summary>
public sealed class ItemContextCounter
{
    public int Games;
    public int Wins;
}

/// <summary>
/// Accumulates one batch of games into the numerators and denominators of the situational
/// item context (#1450), then hands them to the upsert.
/// </summary>
/// <remarks>
/// <para>
/// The <b>whitelist intersection lives here</b>, in one place: an item only ever
/// accumulates against a situation it could mechanically answer, so the counters
/// themselves are already free of the pairs the read would refuse to show. That is what
/// keeps this table at the scale of the matchup pre-aggregation rather than fifteen axes
/// times every item ever completed.
/// </para>
/// <para>
/// Totals, on the other hand, count <b>every</b> axis the game could be placed on, whether
/// or not any item in that slot is eligible for it: a denominator describes the games, not
/// the items, and one shared by every item of the slot is what makes two items' rates
/// comparable.
/// </para>
/// </remarks>
public sealed class ItemContextAccumulator
{
    private readonly Dictionary<ItemContextStatKey, ItemContextCounter> _stats = [];
    private readonly Dictionary<ItemContextTotalKey, ItemContextCounter> _totals = [];
    private readonly HashSet<ItemContextScope> _scopes = [];

    public IReadOnlyDictionary<ItemContextStatKey, ItemContextCounter> Stats => _stats;

    public IReadOnlyDictionary<ItemContextTotalKey, ItemContextCounter> Totals => _totals;

    /// <summary>The slices this batch touched — the scopes whose verdicts have to be rebuilt.</summary>
    public IReadOnlyCollection<ItemContextScope> Scopes => _scopes;

    public bool IsEmpty => _stats.Count == 0 && _totals.Count == 0;

    /// <summary>
    /// Folds one participant's decision in one slot.
    /// </summary>
    /// <param name="scope">Champion, position and patch of the participant.</param>
    /// <param name="slot">Which decision the items below are.</param>
    /// <param name="eligibleAxesByItem">
    /// The items the participant chose in that slot, each with the axes it may be explained
    /// by (<see cref="ItemContextWhitelist"/>). An item with an empty set still counts
    /// towards its pick rate — it simply never gets a sentence.
    /// </param>
    /// <param name="gameAxes">Where this game sits on every axis that could be evaluated.</param>
    /// <param name="win">Whether the participant won.</param>
    public void Add(
        ItemContextScope scope,
        ItemContextSlot slot,
        IReadOnlyDictionary<int, IReadOnlySet<ItemContextAxis>> eligibleAxesByItem,
        IReadOnlyDictionary<ItemContextAxis, ItemContextBucket> gameAxes,
        bool win)
    {
        ArgumentNullException.ThrowIfNull(eligibleAxesByItem);
        ArgumentNullException.ThrowIfNull(gameAxes);

        _scopes.Add(scope);

        AddTotal(scope, slot, ItemContextAxis.Overall, ItemContextBucket.All, win);
        foreach (var (axis, bucket) in gameAxes)
        {
            AddTotal(scope, slot, axis, bucket, win);
        }

        foreach (var (itemId, eligibleAxes) in eligibleAxesByItem)
        {
            if (itemId <= 0)
            {
                continue;
            }

            AddStat(scope, slot, itemId, ItemContextAxis.Overall, ItemContextBucket.All, win);

            foreach (var axis in eligibleAxes)
            {
                if (gameAxes.TryGetValue(axis, out var bucket))
                {
                    AddStat(scope, slot, itemId, axis, bucket, win);
                }
            }
        }
    }

    private void AddTotal(ItemContextScope scope, ItemContextSlot slot, ItemContextAxis axis, ItemContextBucket bucket, bool win)
        => Bump(_totals, new ItemContextTotalKey(scope.ChampionId, scope.Position, scope.Patch, slot, axis, bucket), win);

    private void AddStat(ItemContextScope scope, ItemContextSlot slot, int itemId, ItemContextAxis axis, ItemContextBucket bucket, bool win)
        => Bump(_stats, new ItemContextStatKey(scope.ChampionId, scope.Position, scope.Patch, slot, itemId, axis, bucket), win);

    private static void Bump<TKey>(Dictionary<TKey, ItemContextCounter> target, TKey key, bool win)
        where TKey : notnull
    {
        if (!target.TryGetValue(key, out var counter))
        {
            counter = new ItemContextCounter();
            target[key] = counter;
        }

        counter.Games++;
        if (win)
        {
            counter.Wins++;
        }
    }
}
