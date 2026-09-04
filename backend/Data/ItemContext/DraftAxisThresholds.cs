namespace Data.ItemContext;

/// <summary>
/// Where each axis's Low / Mid / High bands sit (#1450). Bound from configuration, so a
/// band can be moved without a deploy of anything but the ingestor's settings.
/// </summary>
/// <remarks>
/// <para>
/// <b>These defaults are provisional.</b> They were chosen from the shape of each metric,
/// not measured against the champion profiles — the profiles (#1449) only start filling
/// on the first patch after their deploy, so no distribution existed to calibrate
/// against when this shipped. Calibrating them is a follow-up, and the reason it is not a
/// blocker is that a mis-centred band cannot invent a situation: it makes an axis less
/// discriminating (a smaller lift, so fewer verdicts survive the floors), never more.
/// </para>
/// <para>
/// Two shapes only. <b>Shares and magnitudes</b> get a low edge and a high edge and band
/// the value between them. <b>Counts</b> — how many enemies are melee, how many allies
/// build like a frontline — get a "Low at most" and a "High at least", because the honest
/// unit there is a number of champions, not a fraction.
/// </para>
/// </remarks>
public sealed class DraftAxisThresholds
{
    /// <summary>Enemy team magic-damage share: below this the team is not a magic-damage team.</summary>
    public double EnemyMagicShareLow { get; set; } = 0.30;

    /// <summary>Enemy team magic-damage share at or above which the team is magic-heavy.</summary>
    public double EnemyMagicShareHigh { get; set; } = 0.55;

    public double EnemyPhysicalShareLow { get; set; } = 0.35;

    public double EnemyPhysicalShareHigh { get; set; } = 0.65;

    /// <summary>Same bands applied to the lane opponent alone.</summary>
    public double OpponentMagicShareLow { get; set; } = 0.30;

    public double OpponentMagicShareHigh { get; set; } = 0.60;

    public double AllyMagicShareLow { get; set; } = 0.30;

    public double AllyMagicShareHigh { get; set; } = 0.55;

    /// <summary>
    /// Healing plus shielding per minute at or above which a champion counts as a sustain
    /// champion, for the enemy-team count and for the lane-opponent flag alike.
    /// </summary>
    public double SustainChampionPerMinute { get; set; } = 120d;

    /// <summary>Seconds of crowd control per minute at or above which a champion counts as a CC champion.</summary>
    public double CrowdControlChampionPerMinute { get; set; } = 2.5d;

    /// <summary>Share of games completing a purely defensive item at or above which a champion counts as frontline.</summary>
    public double FrontlineChampionRate { get; set; } = 0.50;

    /// <summary>Share of games completing a crit item at or above which a champion counts as a crit carrier.</summary>
    public double CritChampionRate { get; set; } = 0.50;

    /// <summary>Share of games completing an armour-penetration item at or above which a champion counts as one.</summary>
    public double ArmorPenetrationChampionRate { get; set; } = 0.50;

    /// <summary>At most this many enemies of a kind is a Low count; at least <see cref="EnemyCountHigh"/> is High.</summary>
    public int EnemyCountLow { get; set; }

    public int EnemyCountHigh { get; set; } = 2;

    /// <summary>Melee enemies are common, so this axis gets its own, higher band.</summary>
    public int EnemyMeleeCountLow { get; set; } = 1;

    public int EnemyMeleeCountHigh { get; set; } = 4;

    public int AllyFrontlineCountLow { get; set; }

    public int AllyFrontlineCountHigh { get; set; } = 2;

    /// <summary>
    /// The lane opponent's own mean gold lead at 10 minutes, in gold: below the low edge it
    /// is a champion that usually loses lane, at or above the high edge it is a bully.
    /// </summary>
    public double OpponentLanePressureLow { get; set; } = -100d;

    public double OpponentLanePressureHigh { get; set; } = 150d;

    /// <summary>
    /// The champion's own gold lead at 15 minutes. The band edges are the lane-verdict
    /// ones the site already uses for "won / lost the lane" (±300, the outer edge of
    /// <c>web/app/utils/lane-verdict.ts</c>), so "ahead" means the same thing here as it
    /// does everywhere else.
    /// </summary>
    public double OwnGoldLeadLow { get; set; } = -300d;

    public double OwnGoldLeadHigh { get; set; } = 300d;
}
