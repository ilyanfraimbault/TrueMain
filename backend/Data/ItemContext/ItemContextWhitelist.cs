using Data.BuildFacts;

namespace Data.ItemContext;

/// <summary>
/// Which situations an item is even <em>allowed</em> to be explained by (#1450).
///
/// <para>
/// A significant correlation is not an explanation. Over a patch's games, an item's pick
/// rate correlates with plenty of draft features it has no mechanical relationship with —
/// enemy compositions are not independent of one another, and a champion's own popularity
/// drifts with the meta. Left unfiltered, the fold would eventually surface "Zhonya's
/// Hourglass, built when the enemy team is AD-heavy" with a perfectly good p-value, and
/// one sentence like that costs more credibility than ten correct ones earn.
/// </para>
///
/// <para>
/// So eligibility is <b>mechanical, not statistical</b>, and it is derived from the item
/// itself — its CommunityDragon categories and its Grievous Wounds flag — rather than
/// from a list of items somebody maintains. Magic resistance may answer magic damage.
/// Armour may answer physical damage, crit carriers, lethality and melee. Tenacity may
/// answer crowd control. Grievous Wounds may answer sustain. Penetration may answer a
/// frontline. Nothing else is offered, however significant the lift.
/// </para>
///
/// <para>
/// The rule is deliberately asymmetric with the slots. A <b>starter</b> is the lane-opening
/// decision, so every lane-opponent axis is mechanically available to it whatever it is
/// made of. <b>Boots</b> get no such blanket: they go through the same stat-derived rules
/// as any item, which is why Mercury's Treads can be explained by enemy CC and magic
/// damage while Berserker's Greaves — attack speed and nothing else — cannot be explained
/// by the enemy's damage type at all.
/// </para>
/// </summary>
public static class ItemContextWhitelist
{
    private const string Damage = "Damage";
    private const string SpellDamage = "SpellDamage";
    private const string CriticalStrike = "CriticalStrike";
    private const string AttackSpeed = "AttackSpeed";
    private const string OnHit = "OnHit";
    private const string Armor = "Armor";
    private const string SpellBlock = "SpellBlock";
    private const string MagicResist = "MagicResist";
    private const string Tenacity = "Tenacity";
    private const string ArmorPenetration = "ArmorPenetration";
    private const string MagicPenetration = "MagicPenetration";
    private const string Aura = "Aura";

    private static readonly ItemContextAxis[] LaneOpponentAxes =
    [
        ItemContextAxis.OpponentRanged,
        ItemContextAxis.OpponentLanePressure,
        ItemContextAxis.OpponentMagicDamage,
        ItemContextAxis.OpponentSustain,
    ];

    /// <summary>
    /// The axes <paramref name="item"/> may be explained by in <paramref name="slot"/>.
    /// Empty when the item answers no situation this vocabulary can express — it still
    /// gets a class and a pick rate, it just never gets a sentence.
    /// </summary>
    public static IReadOnlySet<ItemContextAxis> For(ItemMetadata item, ItemContextSlot slot)
    {
        ArgumentNullException.ThrowIfNull(item);

        var categories = item.Categories;
        var axes = new HashSet<ItemContextAxis>();

        if (categories.Contains(SpellBlock) || categories.Contains(MagicResist))
        {
            axes.Add(ItemContextAxis.EnemyMagicDamage);
            axes.Add(ItemContextAxis.OpponentMagicDamage);
        }

        if (categories.Contains(Armor))
        {
            axes.Add(ItemContextAxis.EnemyPhysicalDamage);
            axes.Add(ItemContextAxis.EnemyCrit);
            axes.Add(ItemContextAxis.EnemyArmorPenetration);
            axes.Add(ItemContextAxis.EnemyMelee);
        }

        if (categories.Contains(Tenacity))
        {
            axes.Add(ItemContextAxis.EnemyCrowdControl);
        }

        if (item.GrantsGrievousWounds)
        {
            axes.Add(ItemContextAxis.EnemySustain);
            axes.Add(ItemContextAxis.OpponentSustain);
        }

        if (categories.Contains(ArmorPenetration) || categories.Contains(MagicPenetration))
        {
            axes.Add(ItemContextAxis.EnemyFrontline);
        }

        // A defensive item whose defence is health alone — no armour, no magic resistance —
        // answers both damage types, because raw health is what you buy when the threat is
        // everything at once. Resisted items are deliberately NOT covered here: armour and
        // magic resistance each already carry their own damage axis above, and letting a
        // pure magic-resist item also claim "built against physical damage" because it
        // happens to carry health would give back exactly the loose eligibility this class
        // exists to refuse. An item whose health rides with an offensive stat is not
        // covered either: nobody buys Titanic Hydra because the enemy team is AP.
        var isResisted = categories.Contains(Armor)
            || categories.Contains(SpellBlock)
            || categories.Contains(MagicResist);
        if (!isResisted && ItemArchetypes.Classify(item).HasFlag(ItemArchetype.Tank))
        {
            axes.Add(ItemContextAxis.EnemyPhysicalDamage);
            axes.Add(ItemContextAxis.EnemyMagicDamage);
        }

        // The flex-pick axis, both ways round: a champion that can go either way answers
        // "what does my team already have", so an AP item and an AD item are each
        // eligible — at opposite ends of the same axis, which is what the lift measures.
        if (categories.Contains(SpellDamage)
            || categories.Contains(Damage)
            || categories.Contains(CriticalStrike)
            || categories.Contains(AttackSpeed)
            || categories.Contains(OnHit))
        {
            axes.Add(ItemContextAxis.AllyMagicDamage);
        }

        // Team-facing items (the Aura marker) answer what the team is missing.
        if (categories.Contains(Aura))
        {
            axes.Add(ItemContextAxis.AllyFrontline);
        }

        switch (slot)
        {
            case ItemContextSlot.Starter:
                // The starter *is* the answer to the lane, whatever it is made of.
                foreach (var axis in LaneOpponentAxes)
                {
                    axes.Add(axis);
                }

                break;

            case ItemContextSlot.Build:
                // Gold state changes what a completed item costs you to reach, for every
                // item alike — the one axis whose mechanism is universal.
                axes.Add(ItemContextAxis.OwnGoldLeadAt15);
                break;

            case ItemContextSlot.Boots:
            default:
                break;
        }

        return axes;
    }
}
