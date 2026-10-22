namespace Data.ItemContext;

/// <summary>
/// Where the in-game axes of what the enemies have built (#1750) cut into Low / Mid / High.
/// Bound from configuration beside <see cref="DraftAxisThresholds"/>, and mirrored by the API
/// read with the class defaults, like them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Provisional, from the items' own stats, not yet from a measured distribution.</b> The
/// issue asks for bands at measured quantiles, like the gold-lead axis's deciles — and no
/// distribution of "armour the enemies held when this decision opened" existed before this
/// fold measured it. So each run's summary reports the quantiles it observed per axis
/// (<c>BuiltAxisDistribution</c>), and these edges are to be moved onto them after the first
/// re-fold on preprod. Until then they are set in item units: an armour item grants 40 to 80
/// armour, Plated Steelcaps 25, a magic-resist item 40 to 60, Mercury's Treads 20. A mis-set
/// band cannot invent a situation — it makes the axis less discriminating, never wrong.
/// </para>
/// <para>
/// The bands apply at every branch alike, although the enemies have built more by the third
/// item than by the first: a branch's own games are what its rates are compared within, so a
/// deep branch whose games all sit in <c>High</c> simply has no contrast to measure, and the
/// floors drop it rather than misread it.
/// </para>
/// </remarks>
public sealed class BuiltAxisThresholds
{
    /// <summary>Armour summed over the five enemies' completed items: below this, no real armour item on the board.</summary>
    public double EnemyArmorLow { get; set; } = 40d;

    /// <summary>At or above this, the enemy team has built armour in earnest — two or three items' worth.</summary>
    public double EnemyArmorHigh { get; set; } = 120d;

    public double EnemyMagicResistLow { get; set; } = 40d;

    public double EnemyMagicResistHigh { get; set; } = 100d;

    /// <summary>The lane opponent's armour: below this it has built none, not even Plated Steelcaps.</summary>
    public double OpponentArmorLow { get; set; } = 25d;

    /// <summary>At or above this, the lane opponent holds a real armour item.</summary>
    public double OpponentArmorHigh { get; set; } = 60d;

    public double OpponentMagicResistLow { get; set; } = 20d;

    public double OpponentMagicResistHigh { get; set; } = 50d;

    /// <summary>At most this many enemies holding a life-steal or omnivamp item is a Low count.</summary>
    public int EnemyHealingLow { get; set; }

    public int EnemyHealingHigh { get; set; } = 2;

    /// <summary>Share of the enemies' completed damage items that are magic, below which the team built physical.</summary>
    public double EnemyMagicDamageShareLow { get; set; } = 0.30;

    public double EnemyMagicDamageShareHigh { get; set; } = 0.55;

    /// <summary>
    /// Completed damage items the enemies must hold before their split is a share at all: one
    /// item is a coin, not a team's damage profile.
    /// </summary>
    public int MinDamageItems { get; set; } = 2;

    /// <summary>At most this many completed crit items on the enemy team is a Low count.</summary>
    public int EnemyCritLow { get; set; }

    public int EnemyCritHigh { get; set; } = 2;
}
