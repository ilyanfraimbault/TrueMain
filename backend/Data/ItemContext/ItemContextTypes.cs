namespace Data.ItemContext;

/// <summary>
/// Which decision an item context row describes (#1450). The three slots are read from
/// different resolvers and answer different questions, so a verdict never mixes them:
/// "which legendary do I complete", "which boots do I take", "what do I start with".
/// </summary>
public enum ItemContextSlot
{
    /// <summary>A completed legendary item, from <c>FinalBuildResolver</c>.</summary>
    Build,

    /// <summary>The tier-two boots the game settled on, from <c>BootsResolver</c>.</summary>
    Boots,

    /// <summary>One item of the starting basket, from <c>StarterItemAnalyzer</c>.</summary>
    Starter,
}

/// <summary>
/// A draft situation an item's pick rate can be measured against (#1450). Every axis is
/// computed from the <b>measured champion profiles</b> (#1449) of the nine other
/// participants, so it is known at champion select — the point of the feature is to
/// answer "what do I build against this draft", not "what did this game turn into".
/// <see cref="OwnGoldLeadAt15"/> is the one exception and is flagged as in-game.
/// </summary>
public enum ItemContextAxis
{
    /// <summary>
    /// Not a situation: the synthetic axis carrying the item's unconditional counts, so
    /// the pick rate and the conditional rates come out of one table over one cohort.
    /// Always paired with <see cref="ItemContextBucket.All"/>.
    /// </summary>
    Overall,

    /// <summary>Share of the enemy team's damage that is magic, weighted by how much damage each of them deals.</summary>
    EnemyMagicDamage,

    /// <summary>Same for physical damage. Not the complement of the magic share — true damage is neither.</summary>
    EnemyPhysicalDamage,

    /// <summary>How many enemies are healers or shielders.</summary>
    EnemySustain,

    /// <summary>How many enemies lock people down for a long time.</summary>
    EnemyCrowdControl,

    /// <summary>How many enemies build like a frontline.</summary>
    EnemyFrontline,

    /// <summary>How many enemies are melee.</summary>
    EnemyMelee,

    /// <summary>How many enemies usually complete a critical-strike item.</summary>
    EnemyCrit,

    /// <summary>How many enemies usually complete an armour-penetration item.</summary>
    EnemyArmorPenetration,

    /// <summary>Whether the lane opponent is ranged.</summary>
    OpponentRanged,

    /// <summary>
    /// How hard the lane opponent usually wins its lane — its own mean gold lead at 10
    /// minutes, measured across its games. This is what "against a bully" means without
    /// anybody writing down which champions are bullies.
    /// </summary>
    OpponentLanePressure,

    /// <summary>Share of the lane opponent's damage that is magic.</summary>
    OpponentMagicDamage,

    /// <summary>Whether the lane opponent sustains through the lane.</summary>
    OpponentSustain,

    /// <summary>Share of the four allies' damage that is magic — the flex-pick axis.</summary>
    AllyMagicDamage,

    /// <summary>How many allies build like a frontline.</summary>
    AllyFrontline,

    /// <summary>
    /// The champion's own gold lead over its lane opponent at 15 minutes. <b>In-game</b>,
    /// not draft-time: a reader must be told the sentence describes a game state it can
    /// only know once it is there.
    /// </summary>
    OwnGoldLeadAt15,
}

/// <summary>
/// Where a game sits on an axis. Three, not two: the middle is stored but never
/// compared, so a lift is always the contrast between the two ends rather than a split
/// through the middle of the distribution.
/// </summary>
public enum ItemContextBucket
{
    /// <summary>Not a bucket: the single bucket of <see cref="ItemContextAxis.Overall"/>.</summary>
    All,

    Low,

    Mid,

    High,
}

/// <summary>
/// What a verdict says about an item (#1450).
/// </summary>
public enum ItemContextClass
{
    /// <summary>Built in nearly every game — no situation to explain.</summary>
    Core,

    /// <summary>At least one axis moves its pick rate measurably.</summary>
    Situational,

    /// <summary>Built often enough to matter, but no axis moves it: a taste variation.</summary>
    Preference,
}

/// <summary>
/// Facts about the axes themselves, shared by the fold, the verdict builder and the read.
/// </summary>
public static class ItemContextAxes
{
    /// <summary>
    /// Whether a draft alone determines this axis. Everything except
    /// <see cref="ItemContextAxis.OwnGoldLeadAt15"/> is known at champion select, and the
    /// read has to say so — advice you cannot act on until minute 15 is a different kind
    /// of advice.
    /// </summary>
    public static bool IsDraftTime(ItemContextAxis axis) => axis != ItemContextAxis.OwnGoldLeadAt15;

    /// <summary>The real situations, i.e. every axis except the synthetic <see cref="ItemContextAxis.Overall"/>.</summary>
    public static readonly IReadOnlyList<ItemContextAxis> Situational =
        [.. Enum.GetValues<ItemContextAxis>().Where(axis => axis != ItemContextAxis.Overall)];
}
