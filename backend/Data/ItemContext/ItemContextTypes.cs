using System.Collections.Frozen;
using System.Reflection;

namespace Data.ItemContext;

/// <summary>
/// Which decision an item context row describes (#1450). The slots are read from
/// different resolvers and answer different questions, so a verdict never mixes them:
/// "which legendary do I complete", "which boots do I take", "what do I start with", and
/// "when do the boots come".
/// </summary>
public enum ItemContextSlot
{
    /// <summary>A completed legendary item, from <c>FinalBuildResolver</c>.</summary>
    Build,

    /// <summary>The tier-two boots the game settled on, from <c>BootsResolver</c>.</summary>
    Boots,

    /// <summary>One item of the starting basket, from <c>StarterItemAnalyzer</c>.</summary>
    Starter,

    /// <summary>
    /// <b>When</b> the boots were completed, not which (#2101): the row's <c>ItemId</c> is the
    /// step of the build order the boots took — 1 before the first legendary, 2 right after
    /// it, and so on — never an item id. Read by the next-item model only; no verdict is ever
    /// written for it.
    /// </summary>
    BootsTiming,
}

/// <summary>
/// A draft situation an item's pick rate can be measured against (#1450). Every axis is
/// computed from the <b>measured champion profiles</b> (#1449) of the nine other
/// participants, so it is known at champion select — the point of the feature is to
/// answer "what do I build against this draft", not "what did this game turn into".
/// The exceptions carry <see cref="InGameAttribute"/>: <see cref="OwnGoldLeadAt15"/>, and the
/// axes of what the enemies have actually <em>built</em> when a decision opens (#1750).
/// </summary>
/// <remarks>
/// Persisted as its name (<c>HasConversion&lt;string&gt;</c>, 32 characters at most), so a new
/// member costs no migration and the order of the members means nothing.
/// </remarks>
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
    [InGame]
    OwnGoldLeadAt15,

    // What the enemies have built when the decision opens (#1750) — measured by replaying
    // their item timelines to the completion of the item the decision follows. Read by the
    // desktop overlay's next item only, by product decision (2026-10-05): the site's hover
    // cards carry no copy for them yet, so no verdict is written for them.

    /// <summary>Armour on the five enemies' completed items, summed.</summary>
    [InGame(OverlayOnly = true)]
    EnemyArmorBuilt,

    /// <summary>Magic resistance on the five enemies' completed items, summed.</summary>
    [InGame(OverlayOnly = true)]
    EnemyMagicResistBuilt,

    /// <summary>Armour on the lane opponent's completed items.</summary>
    [InGame(OverlayOnly = true)]
    OpponentArmorBuilt,

    /// <summary>Magic resistance on the lane opponent's completed items.</summary>
    [InGame(OverlayOnly = true)]
    OpponentMagicResistBuilt,

    /// <summary>How many enemies hold a completed life-steal or omnivamp item.</summary>
    [InGame(OverlayOnly = true)]
    EnemyHealingBuilt,

    /// <summary>
    /// Share of the enemies' completed damage items that are magic — the flex pick the profile
    /// cannot see, a Kai'Sa gone ability power. Low is a team that built physical.
    /// </summary>
    [InGame(OverlayOnly = true)]
    EnemyMagicDamageBuilt,

    /// <summary>How many critical-strike items the enemies have completed.</summary>
    [InGame(OverlayOnly = true)]
    EnemyCritBuilt,
}

/// <summary>
/// Marks an <see cref="ItemContextAxis"/> a draft alone does not determine: the game has to be
/// under way before a player can know where it sits on it, so a reader must be told the
/// sentence describes a game state, and draft-time findings outrank it (#1449, #1450).
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class InGameAttribute : Attribute
{
    /// <summary>
    /// Folded and read by the overlay's next item, but never turned into a verdict for the
    /// site: the champion page has no wording for the axis (#1750).
    /// </summary>
    public bool OverlayOnly { get; init; }
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
    private static readonly FrozenDictionary<ItemContextAxis, InGameAttribute> InGameAxes =
        Enum.GetValues<ItemContextAxis>()
            .Select(axis => (Axis: axis, Mark: typeof(ItemContextAxis).GetField(axis.ToString())?.GetCustomAttribute<InGameAttribute>()))
            .Where(entry => entry.Mark is not null)
            .ToFrozenDictionary(entry => entry.Axis, entry => entry.Mark!);

    /// <summary>
    /// Whether a draft alone determines this axis — every axis without
    /// <see cref="InGameAttribute"/>. The read has to say so: advice you cannot act on until
    /// minute 15, or until the enemy has built armour, is a different kind of advice.
    /// </summary>
    public static bool IsDraftTime(ItemContextAxis axis) => !InGameAxes.ContainsKey(axis);

    /// <summary>Whether the axis is read by the overlay alone and never gets a site verdict (#1750).</summary>
    public static bool IsOverlayOnly(ItemContextAxis axis) => InGameAxes.TryGetValue(axis, out var mark) && mark.OverlayOnly;

    /// <summary>The real situations, i.e. every axis except the synthetic <see cref="ItemContextAxis.Overall"/>.</summary>
    public static readonly IReadOnlyList<ItemContextAxis> Situational =
        [.. Enum.GetValues<ItemContextAxis>().Where(axis => axis != ItemContextAxis.Overall)];

    /// <summary>The situations a site verdict may carry: <see cref="Situational"/> minus the overlay-only ones.</summary>
    public static readonly IReadOnlyList<ItemContextAxis> OnSite =
        [.. Situational.Where(axis => !IsOverlayOnly(axis))];
}
