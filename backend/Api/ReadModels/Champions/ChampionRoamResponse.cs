namespace TrueMain.ReadModels.Champions;

/// <summary>
/// How much a champion roams at a position: the share of its early-game kill
/// participations (kills + assists before the cutoff) that happened outside its
/// own lane. Higher = roams more. Computed live by classifying each stored kill
/// position against the lane via the map geometry. See issue #536.
/// </summary>
public sealed record ChampionRoamResponse
{
    public int ChampionId { get; init; }
    public string Position { get; init; } = string.Empty;
    public string? Patch { get; init; }
    public int Games { get; init; }
    public int KillParticipations { get; init; }
    public int OutOfLaneParticipations { get; init; }
    /// <summary>Out-of-lane participations / total, in [0, 1]. Null when below the sample floor.</summary>
    public double? OutOfLaneShare { get; init; }
}
