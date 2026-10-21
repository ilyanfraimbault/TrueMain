using Data.BuildFacts;
using Data.Statics;

namespace Data.ItemContext;

/// <summary>What an enemy's completed item says about the game it is in (#1750).</summary>
/// <param name="Armor">Flat armour it grants.</param>
/// <param name="MagicResist">Flat magic resistance it grants.</param>
/// <param name="Damage">Which damage type it builds towards, if any.</param>
/// <param name="Crit">Whether it grants critical-strike chance.</param>
/// <param name="Healing">Whether it heals its holder off damage: life steal or omnivamp.</param>
public readonly record struct BuiltItemFacts(
    double Armor,
    double MagicResist,
    BuiltDamage Damage,
    bool Crit,
    bool Healing);

/// <summary>The damage type an item builds towards.</summary>
public enum BuiltDamage
{
    /// <summary>A defensive or utility item: it says nothing about what its holder threatens.</summary>
    None,

    Physical,

    Magic,
}

/// <summary>
/// The items of one patch that count as <b>built</b> for the in-game axes (#1750), with the
/// facts the axes read off them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only completed items count.</b> A component is a step towards an item, not a decision
/// about the game: an enemy holding a Cloth Armor is three hundred gold from anything, and an
/// enemy holding two of them is usually building one item, not two. The completed set is the
/// build path's own — <see cref="FinalBuildResolver.IsEligibleFinalBuildItem"/>, plus the
/// completed boots (Plated Steelcaps is armour a player has to go through) — plus the item an
/// inventory transform starts from (Manamune before it turns into Muramana), since that
/// purchase is the completion the timeline records.
/// </para>
/// <para>
/// <b>The figures are Data Dragon's</b> (<see cref="IItemStatsProvider"/>), the categories
/// CommunityDragon's: an item one of them does not know is not in the catalogue, and an enemy
/// holding it is read as holding nothing there rather than a guess.
/// </para>
/// </remarks>
public sealed class BuiltItemCatalog
{
    private const string SpellVamp = "SpellVamp";
    private const string LifeSteal = "LifeSteal";

    private readonly Dictionary<int, BuiltItemFacts> _items;

    private BuiltItemCatalog(Dictionary<int, BuiltItemFacts> items) => _items = items;

    public static BuiltItemCatalog Empty { get; } = new([]);

    public int Count => _items.Count;

    public bool TryGet(int itemId, out BuiltItemFacts facts) => _items.TryGetValue(itemId, out facts);

    public static BuiltItemCatalog From(
        IReadOnlyDictionary<int, ItemMetadata> metadata,
        IReadOnlyDictionary<int, ItemStats> stats)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(stats);

        var completed = new HashSet<int>();
        foreach (var item in metadata.Values)
        {
            if (FinalBuildResolver.IsEligibleFinalBuildItem(item) || item.IsFinalBoots)
            {
                completed.Add(FinalBuildResolver.GetDisplayedBuildItemId(item));
            }
        }

        var items = new Dictionary<int, BuiltItemFacts>();
        foreach (var itemId in completed)
        {
            if (stats.TryGetValue(itemId, out var itemStats))
            {
                items[itemId] = Facts(itemStats);
            }
        }

        return new BuiltItemCatalog(items);
    }

    /// <summary>
    /// An item builds towards magic damage when it carries ability power at least equal to its
    /// attack damage (Hextech Gunblade, Nashor's Tooth), towards physical damage when it carries
    /// attack damage, crit or attack speed otherwise — the stats a physical carrier builds.
    /// </summary>
    private static BuiltItemFacts Facts(ItemStats stats)
    {
        var damage = stats.AbilityPower > 0 && stats.AbilityPower >= stats.AttackDamage
            ? BuiltDamage.Magic
            : stats.AttackDamage > 0 || stats.CritChance > 0 || stats.AttackSpeed > 0
                ? BuiltDamage.Physical
                : BuiltDamage.None;

        return new BuiltItemFacts(
            stats.Armor,
            stats.MagicResist,
            damage,
            stats.CritChance > 0,
            stats.LifeSteal > 0 || stats.Tags.Contains(LifeSteal) || stats.Tags.Contains(SpellVamp));
    }
}
