namespace Data.Statics;

/// <summary>
/// The stats an item grants, read from Data Dragon's <c>item.json</c> (#1750): the numbers the
/// in-game item-context axes sum over what the enemies have completed. CommunityDragon's
/// catalogue carries categories but no figures, so this is the one place they come from.
/// </summary>
/// <param name="ItemId">Riot's item id.</param>
/// <param name="Armor">Flat armour.</param>
/// <param name="MagicResist">Flat magic resistance.</param>
/// <param name="AttackDamage">Flat attack damage.</param>
/// <param name="AbilityPower">Flat ability power.</param>
/// <param name="CritChance">Critical-strike chance, as a fraction (0.25 for 25%).</param>
/// <param name="AttackSpeed">Bonus attack speed, as a fraction.</param>
/// <param name="LifeSteal">Life steal, as a fraction.</param>
/// <param name="Tags">Data Dragon's tags verbatim (<c>"SpellVamp"</c> marks omnivamp, which has no stat key).</param>
public sealed record ItemStats(
    int ItemId,
    double Armor,
    double MagicResist,
    double AttackDamage,
    double AbilityPower,
    double CritChance,
    double AttackSpeed,
    double LifeSteal,
    IReadOnlySet<string> Tags);
