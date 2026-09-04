namespace Data.Entities;

/// <summary>
/// What a champion <em>does</em> in its games, measured over its own games (#1449):
/// how its damage splits by type, how much it heals and shields, how long it locks
/// people down, how much of its team's damage it absorbs, how its lane goes at 10 and
/// 15 minutes, and which item archetypes it completes. One row per
/// <c>(champion, position, patch)</c>, holding <b>additive sums</b> plus the games
/// behind each family of sums; every share, mean and per-minute figure is derived at
/// read time, so the fold stays a one-pass <c>ON CONFLICT ... + EXCLUDED</c> upsert
/// and frozen patches keep the numbers they were folded with (#466).
///
/// <para>
/// <b>Why measured, not labelled.</b> These rows are the dictionary that turns a draft
/// into situational axes for #1450 ("the enemy team is AP-heavy", "the lane opponent is
/// a bully"). A hand-written champion list would be stale after every rework and would
/// encode one person's opinion of what a champion is; a profile folded from the games
/// updates on its own and says what the champion actually did.
/// </para>
///
/// <para>
/// <b>Full pool, not the champion cohort.</b> A profile describes the champion, not its
/// mains, so every participant with the data counts (harvested rows included); the
/// only exclusions are remakes and non-canonical positions, the whole-match rules of
/// <c>Data.Aggregation.ChampionCohort</c>.
/// </para>
///
/// <para>
/// <b>Only participants carrying the #1448 context fields are folded.</b> A row ingested
/// before those columns existed reads <c>NULL</c> there and contributes nothing — not a
/// zero — so the profiles fill in over the first patch after that deploy rather than
/// being diluted by unmeasured games. The lane and item families have their own
/// sub-counters because their sources (timeline snapshots, item metadata) can be
/// missing for a game the context fields were measured on.
/// </para>
/// </summary>
public class ChampionProfileStat
{
    public Guid Id { get; set; }

    public int ChampionId { get; set; }

    /// <summary>Canonical <c>TeamPosition</c> (TOP / JUNGLE / MIDDLE / BOTTOM / UTILITY).</summary>
    public string Position { get; set; } = string.Empty;

    /// <summary>Canonical major.minor patch (e.g. "16.4").</summary>
    public string Patch { get; set; } = string.Empty;

    /// <summary>Participants folded — the denominator of every context sum below.</summary>
    public int Games { get; set; }

    public int Wins { get; set; }

    /// <summary>Sum of the games' durations, the denominator of every per-minute figure.</summary>
    public long GameDurationSecondsSum { get; set; }

    public long PhysicalDamageToChampionsSum { get; set; }

    public long MagicDamageToChampionsSum { get; set; }

    public long TrueDamageToChampionsSum { get; set; }

    public long TotalHealSum { get; set; }

    public long HealsOnTeammatesSum { get; set; }

    public long DamageShieldedOnTeammatesSum { get; set; }

    public long TimeCCingOthersSum { get; set; }

    public long TotalTimeCCDealtSum { get; set; }

    public long DamageTakenSum { get; set; }

    public long DamageSelfMitigatedSum { get; set; }

    /// <summary>
    /// Games where all five teammates carried the context fields, so the team's damage
    /// taken could be summed — the denominator of the frontline share
    /// (<see cref="DamageTakenSum"/> over <see cref="TeamDamageTakenSum"/> on those games).
    /// </summary>
    public int TeamDamageTakenGames { get; set; }

    /// <summary>Damage taken by the whole team, summed over <see cref="TeamDamageTakenGames"/>.</summary>
    public long TeamDamageTakenSum { get; set; }

    /// <summary>
    /// Games where both the champion and its lane opponent had a 10-minute snapshot —
    /// the denominator of the 10-minute lead sums and of <see cref="KillsBy10Sum"/>.
    /// </summary>
    public int LaneGamesAt10 { get; set; }

    /// <summary>Signed gold lead over the lane opponent at 10 minutes, summed.</summary>
    public long GoldLeadAt10Sum { get; set; }

    public long XpLeadAt10Sum { get; set; }

    /// <summary>Champion kills by the 10-minute mark, summed over <see cref="LaneGamesAt10"/>.</summary>
    public int KillsBy10Sum { get; set; }

    public int LaneGamesAt15 { get; set; }

    public long GoldLeadAt15Sum { get; set; }

    public long XpLeadAt15Sum { get; set; }

    /// <summary>
    /// Games where item metadata resolved for the patch, so the final inventory could be
    /// classified — the denominator of the archetype counters below.
    /// </summary>
    public int ItemGames { get; set; }

    /// <summary>Games where the final inventory held at least one completed crit item.</summary>
    public int CritGames { get; set; }

    /// <summary>Games with at least one completed armour-penetration AD item (lethality or % pen).</summary>
    public int ArmorPenetrationGames { get; set; }

    public int OnHitGames { get; set; }

    public int AbilityPowerGames { get; set; }

    /// <summary>Games with at least one completed pure-defensive item (resistances or health, no offence).</summary>
    public int TankGames { get; set; }

    /// <summary>
    /// Whether the champion's base attack range makes it ranged, from Data Dragon. A
    /// static attribute rather than a sum: written whenever the fold could resolve it and
    /// kept (<c>COALESCE</c>) when it could not, so a Data Dragon outage never blanks it.
    /// <see langword="null"/> until resolved once.
    /// </summary>
    public bool? IsRanged { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
