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
    int ParentItemId,
    int ItemId,
    ItemContextAxis Axis,
    ItemContextBucket Bucket);

/// <summary>The grain of a <c>champion_item_context_totals</c> row.</summary>
public readonly record struct ItemContextTotalKey(
    int ChampionId,
    string Position,
    string Patch,
    ItemContextSlot Slot,
    int ParentItemId,
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
/// <b>A denominator is a branch, not a slot (#1496).</b> A game counts towards the total of
/// every branch it made a decision on — the branch it started from, then the branch of each
/// item it went on to leave for another one — and towards no other. A game that reached an
/// item and ended there made no decision after it, so it is in none of that item's branch
/// totals. This is what makes the siblings of a branch share one denominator, and what stops
/// an axis from lifting all of them at once: "ahead at 15 minutes" cannot recommend an item
/// merely because the game lasted long enough to buy it, since the games that lasted are
/// exactly the games in the denominator.
/// </para>
/// <para>
/// Totals still count <b>every</b> axis the game could be placed on, whether or not any item
/// on that branch is eligible for it: a denominator describes the games, not the items, and
/// one shared by every sibling is what makes two siblings' rates comparable.
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
    /// Folds one participant's decisions in one slot.
    /// </summary>
    /// <param name="scope">Champion, position and patch of the participant.</param>
    /// <param name="slot">Which decision the steps below are.</param>
    /// <param name="edges">
    /// The steps the participant took in that slot (<see cref="ItemContextSlotResolver"/>),
    /// each with the axes it may be explained by. An edge whose item has an empty axis set
    /// still counts towards its pick rate — it simply never gets a sentence.
    /// </param>
    /// <param name="gameAxes">Where this game sits on every axis that could be evaluated.</param>
    /// <param name="win">Whether the participant won.</param>
    public void Add(
        ItemContextScope scope,
        ItemContextSlot slot,
        IReadOnlyList<ItemContextEdge> edges,
        IReadOnlyDictionary<ItemContextAxis, ItemContextBucket> gameAxes,
        bool win)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(gameAxes);

        _scopes.Add(scope);

        // One branch may carry several of this game's items — the starter basket is the
        // case — and its denominator counts games, not items, so each branch is counted once
        // however many of its children this game took.
        foreach (var parentItemId in edges.Select(edge => edge.ParentItemId).Distinct())
        {
            AddTotal(scope, slot, parentItemId, ItemContextAxis.Overall, ItemContextBucket.All, win);
            foreach (var (axis, bucket) in gameAxes)
            {
                AddTotal(scope, slot, parentItemId, axis, bucket, win);
            }
        }

        foreach (var edge in edges)
        {
            if (edge.ItemId <= 0)
            {
                continue;
            }

            AddStat(scope, slot, edge, ItemContextAxis.Overall, ItemContextBucket.All, win);

            foreach (var axis in edge.EligibleAxes)
            {
                if (gameAxes.TryGetValue(axis, out var bucket))
                {
                    AddStat(scope, slot, edge, axis, bucket, win);
                }
            }
        }
    }

    private void AddTotal(
        ItemContextScope scope,
        ItemContextSlot slot,
        int parentItemId,
        ItemContextAxis axis,
        ItemContextBucket bucket,
        bool win)
        => Bump(
            _totals,
            new ItemContextTotalKey(scope.ChampionId, scope.Position, scope.Patch, slot, parentItemId, axis, bucket),
            win);

    private void AddStat(
        ItemContextScope scope,
        ItemContextSlot slot,
        ItemContextEdge edge,
        ItemContextAxis axis,
        ItemContextBucket bucket,
        bool win)
        => Bump(
            _stats,
            new ItemContextStatKey(
                scope.ChampionId, scope.Position, scope.Patch, slot, edge.ParentItemId, edge.ItemId, axis, bucket),
            win);

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
