namespace TrueMain.ReadModels.Champions;

/// <summary>
/// How much a champion roams at a position: the average number of out-of-lane
/// kill participations (kills + assists) per game, taken at the 5/10/15-minute
/// marks (cumulative). A roam is a participation in a different lane, the enemy
/// jungle, or the enemy base — the river and the player's own-side jungle do not
/// count (see <see cref="Core.Lol.Map.LolMap.IsRoam"/>). Computed live by
/// classifying each stored kill position against the lane and team side. The
/// per-game averages are null below the sample floor, and for JUNGLE (which has
/// no meaningful own lane). See issue #536.
/// </summary>
public sealed record ChampionRoamResponse
{
    public int ChampionId { get; init; }
    public string Position { get; init; } = string.Empty;
    public string? Patch { get; init; }

    /// <summary>Timeline-covered games the champion played in this lane — the per-game average denominator.</summary>
    public int Games { get; init; }

    /// <summary>Average out-of-lane kill participations per game by the 5-minute mark. Null below the floor / for JUNGLE.</summary>
    public double? RoamKp5 { get; init; }

    /// <summary>Average out-of-lane kill participations per game by the 10-minute mark (includes the @5 window).</summary>
    public double? RoamKp10 { get; init; }

    /// <summary>Average out-of-lane kill participations per game by the 15-minute mark (includes the @10 window).</summary>
    public double? RoamKp15 { get; init; }
}
