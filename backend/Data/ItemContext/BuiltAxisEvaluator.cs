namespace Data.ItemContext;

/// <summary>
/// What the enemies have built at one instant, from the point of view of one participant (#1750).
/// </summary>
/// <param name="Enemies">
/// The completed items each of the five enemies held, or null when one of them could not be
/// read: a team-wide sum missing a member is not a smaller sum, it is an unknown one.
/// </param>
/// <param name="LaneOpponent">The lane opponent's completed items, or null when there is none or it could not be read.</param>
public readonly record struct BuiltContext(
    IReadOnlyList<IReadOnlyList<int>>? Enemies,
    IReadOnlyList<int>? LaneOpponent);

/// <summary>
/// Turns what the enemies have built into the banded in-game situations (#1750). Pure, and
/// shared by the fold (inventories replayed from the timelines) and the next-item read
/// (inventories the live game reports), so a game is placed on these axes the same way on
/// both sides.
/// </summary>
/// <remarks>
/// Like <see cref="DraftAxisEvaluator"/>, an axis it cannot compute is absent, never defaulted.
/// </remarks>
public static class BuiltAxisEvaluator
{
    public static IReadOnlyDictionary<ItemContextAxis, ItemContextBucket> Evaluate(
        BuiltContext context,
        BuiltItemCatalog catalog,
        BuiltAxisThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(thresholds);

        return Bucket(Measure(context, catalog, thresholds), thresholds);
    }

    /// <summary>Bands values <see cref="Measure"/> returned.</summary>
    public static IReadOnlyDictionary<ItemContextAxis, ItemContextBucket> Bucket(
        IReadOnlyList<(ItemContextAxis Axis, double Value)> values,
        BuiltAxisThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(thresholds);

        var axes = new Dictionary<ItemContextAxis, ItemContextBucket>();
        foreach (var (axis, value) in values)
        {
            axes[axis] = Band(axis, value, thresholds);
        }

        return axes;
    }

    /// <summary>
    /// The raw value of every axis the context can carry, before banding — what the fold's
    /// distribution report reads, so the bands can be moved onto measured quantiles.
    /// </summary>
    public static IReadOnlyList<(ItemContextAxis Axis, double Value)> Measure(
        BuiltContext context,
        BuiltItemCatalog catalog,
        BuiltAxisThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(thresholds);

        var values = new List<(ItemContextAxis, double)>();

        if (context.Enemies is { Count: > 0 } enemies)
        {
            var facts = enemies.Select(items => Facts(items, catalog)).ToList();
            var all = facts.SelectMany(member => member).ToList();

            values.Add((ItemContextAxis.EnemyArmorBuilt, all.Sum(item => item.Armor)));
            values.Add((ItemContextAxis.EnemyMagicResistBuilt, all.Sum(item => item.MagicResist)));
            values.Add((ItemContextAxis.EnemyHealingBuilt, facts.Count(member => member.Any(item => item.Healing))));
            values.Add((ItemContextAxis.EnemyCritBuilt, all.Count(item => item.Crit)));

            var magic = all.Count(item => item.Damage == BuiltDamage.Magic);
            var physical = all.Count(item => item.Damage == BuiltDamage.Physical);
            var damageItems = magic + physical;
            if (damageItems > 0 && damageItems >= thresholds.MinDamageItems)
            {
                values.Add((ItemContextAxis.EnemyMagicDamageBuilt, magic / (double)damageItems));
            }
        }

        if (context.LaneOpponent is { } opponent)
        {
            var facts = Facts(opponent, catalog);
            values.Add((ItemContextAxis.OpponentArmorBuilt, facts.Sum(item => item.Armor)));
            values.Add((ItemContextAxis.OpponentMagicResistBuilt, facts.Sum(item => item.MagicResist)));
        }

        return values;
    }

    private static List<BuiltItemFacts> Facts(IReadOnlyList<int> items, BuiltItemCatalog catalog)
    {
        var facts = new List<BuiltItemFacts>();
        foreach (var itemId in items)
        {
            if (catalog.TryGet(itemId, out var item))
            {
                facts.Add(item);
            }
        }

        return facts;
    }

    private static ItemContextBucket Band(ItemContextAxis axis, double value, BuiltAxisThresholds thresholds)
        => axis switch
        {
            ItemContextAxis.EnemyArmorBuilt => Edges(value, thresholds.EnemyArmorLow, thresholds.EnemyArmorHigh),
            ItemContextAxis.EnemyMagicResistBuilt => Edges(value, thresholds.EnemyMagicResistLow, thresholds.EnemyMagicResistHigh),
            ItemContextAxis.OpponentArmorBuilt => Edges(value, thresholds.OpponentArmorLow, thresholds.OpponentArmorHigh),
            ItemContextAxis.OpponentMagicResistBuilt => Edges(value, thresholds.OpponentMagicResistLow, thresholds.OpponentMagicResistHigh),
            ItemContextAxis.EnemyMagicDamageBuilt => Edges(value, thresholds.EnemyMagicDamageShareLow, thresholds.EnemyMagicDamageShareHigh),
            ItemContextAxis.EnemyHealingBuilt => Count(value, thresholds.EnemyHealingLow, thresholds.EnemyHealingHigh),
            ItemContextAxis.EnemyCritBuilt => Count(value, thresholds.EnemyCritLow, thresholds.EnemyCritHigh),
            _ => throw new ArgumentOutOfRangeException(nameof(axis), axis, "Not an axis of what the enemies built."),
        };

    private static ItemContextBucket Edges(double value, double lowEdge, double highEdge)
        => value < lowEdge
            ? ItemContextBucket.Low
            : value >= highEdge
                ? ItemContextBucket.High
                : ItemContextBucket.Mid;

    private static ItemContextBucket Count(double count, int lowAtMost, int highAtLeast)
        => count <= lowAtMost
            ? ItemContextBucket.Low
            : count >= highAtLeast
                ? ItemContextBucket.High
                : ItemContextBucket.Mid;
}
