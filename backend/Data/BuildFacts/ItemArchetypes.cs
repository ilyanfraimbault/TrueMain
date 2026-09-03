namespace Data.BuildFacts;

/// <summary>
/// The item archetypes a champion's final inventory is classified into (#1449) — a
/// bit set, since one item can belong to several and one inventory usually does.
/// </summary>
[Flags]
public enum ItemArchetype
{
    None = 0,

    /// <summary>A completed critical-strike item (Infinity Edge, Collector, ...).</summary>
    Crit = 1 << 0,

    /// <summary>
    /// A completed AD item granting armour penetration — lethality (Youmuu's, Edge of
    /// Night, ...) or percentage (Serylda's, Lord Dominik's, Black Cleaver). Not split
    /// further: CommunityDragon files both under <c>ArmorPenetration</c>.
    /// </summary>
    ArmorPenetration = 1 << 1,

    /// <summary>A completed on-hit item (Blade of the Ruined King, Wit's End, Kraken Slayer, ...).</summary>
    OnHit = 1 << 2,

    /// <summary>A completed ability-power item.</summary>
    AbilityPower = 1 << 3,

    /// <summary>
    /// A completed purely defensive item: resistances and/or health with none of the
    /// offensive categories (attack damage, ability power, crit, attack speed). Sunfire,
    /// Frozen Heart, Spirit Visage, Warmog's qualify; Titanic Hydra or Rylai's do not,
    /// because their health rides with an offensive stat.
    /// </summary>
    Tank = 1 << 4,
}

/// <summary>
/// Classifies items into <see cref="ItemArchetype"/>s from their CommunityDragon
/// categories, so "this champion builds crit" is read from what it actually completed
/// rather than from a hand-kept list. Only completed non-boots, non-starter, non-quest
/// items are classified: a Doran's Blade is <c>Damage</c> too, and a starter is not a
/// build choice.
/// </summary>
public static class ItemArchetypes
{
    private const string Damage = "Damage";
    private const string SpellDamage = "SpellDamage";
    private const string CriticalStrike = "CriticalStrike";
    private const string AttackSpeed = "AttackSpeed";
    private const string ArmorPenetration = "ArmorPenetration";
    private const string OnHit = "OnHit";
    private const string Armor = "Armor";
    private const string SpellBlock = "SpellBlock";
    private const string Health = "Health";

    /// <summary>Whether the item is a completed build item whose archetype is worth reading.</summary>
    public static bool IsClassifiable(ItemMetadata item)
        => item.IsFinalItem
            && !item.IsBootsItem
            && !item.IsConsumable
            && !item.IsStarterClassItem
            && !item.IsSupportQuestStarter
            && !item.IsSupportQuestIntermediate
            && !item.IsSupportQuestCompletion;

    /// <summary>The archetypes of one item; <see cref="ItemArchetype.None"/> when it is not classifiable.</summary>
    public static ItemArchetype Classify(ItemMetadata item)
    {
        if (!IsClassifiable(item))
        {
            return ItemArchetype.None;
        }

        var categories = item.Categories;
        var result = ItemArchetype.None;

        if (categories.Contains(CriticalStrike))
        {
            result |= ItemArchetype.Crit;
        }

        if (categories.Contains(ArmorPenetration) && categories.Contains(Damage) && !categories.Contains(SpellDamage))
        {
            result |= ItemArchetype.ArmorPenetration;
        }

        if (categories.Contains(OnHit))
        {
            result |= ItemArchetype.OnHit;
        }

        if (categories.Contains(SpellDamage))
        {
            result |= ItemArchetype.AbilityPower;
        }

        var defensive = categories.Contains(Armor) || categories.Contains(SpellBlock) || categories.Contains(Health);
        var offensive = categories.Contains(Damage)
            || categories.Contains(SpellDamage)
            || categories.Contains(CriticalStrike)
            || categories.Contains(AttackSpeed);
        if (defensive && !offensive)
        {
            result |= ItemArchetype.Tank;
        }

        return result;
    }

    /// <summary>
    /// The union of archetypes over a final inventory. Ids the metadata does not know
    /// (an empty slot, a trinket, an item the patch branch lacks) contribute nothing.
    /// </summary>
    public static ItemArchetype ClassifyInventory(
        ReadOnlySpan<int> itemIds,
        IReadOnlyDictionary<int, ItemMetadata> metadata)
    {
        var result = ItemArchetype.None;
        foreach (var itemId in itemIds)
        {
            if (itemId > 0 && metadata.TryGetValue(itemId, out var item))
            {
                result |= Classify(item);
            }
        }

        return result;
    }
}
