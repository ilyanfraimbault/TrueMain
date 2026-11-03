using Data.BuildFacts;

namespace Data.ItemContext;

/// <summary>
/// The situational needs a completed item answers (#2371): what a player who bought it
/// instead of the mains' choice has already covered.
/// </summary>
[Flags]
public enum ItemNeed
{
    None = 0,
    MagicResist = 1 << 0,
    Armor = 1 << 1,
    AntiHeal = 1 << 2,
    ArmorPenetration = 1 << 3,
    MagicPenetration = 1 << 4,
    Tenacity = 1 << 5,
}

/// <summary>
/// The needs of a patch's completed items (#2371). <see cref="Needs"/> reads any candidate's —
/// legendaries and completed boots, so Mercury's Treads is dropped once magic resistance is
/// covered — while only the <see cref="Covering"/> items, the legendaries, cover a need when
/// bought off advice: a component is a step towards an item, and boots are their own slot.
/// </summary>
public sealed record ItemNeedCatalog(IReadOnlyDictionary<int, ItemNeed> Needs, IReadOnlySet<int> Covering)
{
    public static ItemNeedCatalog Empty { get; } = new(new Dictionary<int, ItemNeed>(), new HashSet<int>());
}

/// <summary>
/// Reads an item's <see cref="ItemNeed"/>s off the same attributes the situational whitelist
/// (<see cref="ItemContextWhitelist"/>) reads: CommunityDragon's categories and the Grievous
/// Wounds flag. Damage and health are no need here: every legendary carries one or the other,
/// and treating them as covered would leave nothing to suggest.
/// </summary>
public static class ItemNeeds
{
    private const string Armor = "Armor";
    private const string SpellBlock = "SpellBlock";
    private const string MagicResist = "MagicResist";
    private const string Tenacity = "Tenacity";
    private const string ArmorPenetration = "ArmorPenetration";
    private const string MagicPenetration = "MagicPenetration";

    public static ItemNeed Of(ItemMetadata item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var categories = item.Categories;
        var needs = ItemNeed.None;

        if (categories.Contains(SpellBlock) || categories.Contains(MagicResist))
        {
            needs |= ItemNeed.MagicResist;
        }

        if (categories.Contains(Armor))
        {
            needs |= ItemNeed.Armor;
        }

        if (item.GrantsGrievousWounds)
        {
            needs |= ItemNeed.AntiHeal;
        }

        if (categories.Contains(ArmorPenetration))
        {
            needs |= ItemNeed.ArmorPenetration;
        }

        if (categories.Contains(MagicPenetration))
        {
            needs |= ItemNeed.MagicPenetration;
        }

        if (categories.Contains(Tenacity))
        {
            needs |= ItemNeed.Tenacity;
        }

        return needs;
    }

    /// <summary>
    /// The needs of every completed legendary and completed boots of a patch, keyed by item id,
    /// with the legendaries marked as the items that can cover one (<see cref="ItemNeedCatalog"/>).
    /// </summary>
    public static ItemNeedCatalog Catalog(IReadOnlyDictionary<int, ItemMetadata> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var needs = new Dictionary<int, ItemNeed>();
        var covering = new HashSet<int>();
        foreach (var item in metadata.Values)
        {
            if (FinalBuildResolver.IsEligibleFinalBuildItem(item))
            {
                var itemId = FinalBuildResolver.GetDisplayedBuildItemId(item);
                needs[itemId] = Of(item);
                covering.Add(itemId);
            }
            else if (item.IsFinalBoots)
            {
                needs[item.Id] = Of(item);
            }
        }

        return new ItemNeedCatalog(needs, covering);
    }
}
