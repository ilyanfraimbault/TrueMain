namespace Core.Lol.Performance;

/// <summary>
/// Everything <see cref="PerformanceScore"/> needs to grade a single
/// participant. Deliberately a plain value bag of numbers the database already
/// stores — end-of-game totals from <c>match_participants</c> plus the @15
/// timeline diffs derived from <c>match_participant_timeline_snapshots</c> — so
/// the scoring stays a pure function with no persistence or Riot-API coupling.
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

    /// <summary>CS lead over the lane opponent @15. Null when there is no @15 snapshot on either side.</summary>
    public int? CsDiff15 { get; init; }

    /// <summary>Gold lead over the lane opponent @15. Null when there is no @15 snapshot on either side.</summary>
    public int? GoldDiff15 { get; init; }

    /// <summary>XP lead over the lane opponent @15. Null when there is no @15 snapshot on either side.</summary>
    public int? XpDiff15 { get; init; }
}
