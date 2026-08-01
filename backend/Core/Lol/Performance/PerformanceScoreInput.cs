namespace Core.Lol.Performance;

/// <summary>
/// One canonical timeline mark's lead over the lane opponent — the same
/// participant-vs-participant comparison the match detail page already shows at
/// @15, generalised to every mark the ingestor stores
/// (<c>match_participant_timeline_snapshots</c>: 5 / 10 / 15 / 20 / 30).
///
/// <para>A mark only exists when <em>both</em> sides have a snapshot at it, so a
/// game that ended at 22 minutes simply carries fewer marks — never a zeroed
/// one.</para>
/// </summary>
/// <param name="Minute">The canonical mark this lead was measured at (5, 10, 15, 20, 30).</param>
/// <param name="GoldDiff">Total-gold lead over the lane opponent at that mark.</param>
/// <param name="CsDiff">Lane minions + neutral monsters lead at that mark.</param>
/// <param name="XpDiff">Experience lead at that mark.</param>
public readonly record struct LaneLead(int Minute, int GoldDiff, int CsDiff, int XpDiff);

/// <summary>
/// Everything <see cref="PerformanceScore"/> needs to grade a single
/// participant. Deliberately a plain value bag of numbers the database already
/// stores — end-of-game totals from <c>match_participants</c>, the per-mark
/// timeline leads derived from <c>match_participant_timeline_snapshots</c>, and
/// the early out-of-lane takedown count derived from
/// <c>match_participant_kill_positions</c> — so the scoring stays a pure
/// function with no persistence or Riot-API coupling.
/// </summary>
public sealed record PerformanceScoreInput
{
    /// <summary>
    /// Riot team position (TOP / JUNGLE / MIDDLE / BOTTOM / UTILITY). Selects the
    /// role weight profile; anything else falls back to the neutral profile.
    /// </summary>
    public string TeamPosition { get; init; } = string.Empty;

    public int Kills { get; init; }

    public int Deaths { get; init; }

    public int Assists { get; init; }

    /// <summary>Total kills scored by this participant's team. 0 disables the kill-participation component.</summary>
    public int TeamKills { get; init; }

    public int DamageToChampions { get; init; }

    /// <summary>Sum of the team's damage to champions. 0 disables the damage-share component.</summary>
    public int TeamDamageToChampions { get; init; }

    public int GoldEarned { get; init; }

    /// <summary>Sum of the team's earned gold. 0 disables the gold-share component.</summary>
    public int TeamGoldEarned { get; init; }

    /// <summary>Lane minions + neutral monsters.</summary>
    public int Cs { get; init; }

    public int VisionScore { get; init; }

    /// <summary>Game length in minutes. 0 or less disables every per-minute component.</summary>
    public double GameDurationMinutes { get; init; }

    /// <summary>
    /// Leads over the lane opponent at each canonical timeline mark both sides
    /// have a snapshot for. Marks at or before
    /// <see cref="PerformanceScore.LaningPhaseLastMinute"/> feed the laning
    /// component, later ones feed the mid-game component; an empty list drops
    /// both. Order is irrelevant and duplicate minutes are summed as separate
    /// marks, so the caller does not have to sort or de-duplicate.
    /// </summary>
    public IReadOnlyList<LaneLead> LaneLeads { get; init; } = Array.Empty<LaneLead>();

    /// <summary>
    /// Kill participations (kills + assists) this player took part in outside
    /// their own lane during the early game — the same
    /// <c>Core.Lol.Map.LolMap.IsRoam</c> classification the champion roam panel
    /// uses, over the bounded <c>match_participant_kill_positions</c> rows.
    ///
    /// <para><c>null</c> means "this match has no kill-position coverage", which
    /// drops the roam component instead of scoring a 0. A match that <em>is</em>
    /// covered and in which the player never left their lane is a genuine
    /// <c>0</c>.</para>
    /// </summary>
    public int? OutOfLaneTakedowns { get; init; }
}
