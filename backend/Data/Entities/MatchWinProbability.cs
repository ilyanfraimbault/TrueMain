namespace Data.Entities;

/// <summary>
/// A match's post-game win-probability curve and turning points (#1911), computed once at
/// timeline ingest by <c>Core.Lol.WinProbability.WinProbabilityBuilder</c> and read whole by
/// the match detail. One row per match, keyed by the match id; no row for a game the builder
/// gives no curve (under fifteen minutes, lanes not paired) or ingested before #1911.
/// The three payloads ride as <c>jsonb</c> like <see cref="MatchParticipant.ItemEvents"/>:
/// read as a whole, never filtered on in SQL. Dies with its match (cascade).
/// </summary>
public class MatchWinProbability
{
    public string MatchId { get; set; } = string.Empty;

    /// <summary>Team 100's chance at every timeline frame and at the end of the game.</summary>
    public List<MatchWinProbabilityPoint> Points { get; set; } = [];

    /// <summary>The events that moved the chance most, largest |delta| first.</summary>
    public List<MatchWinProbabilitySwing> Swings { get; set; } = [];

    /// <summary>Every epic monster taken, unweighed ones included.</summary>
    public List<MatchWinProbabilityObjective> Objectives { get; set; } = [];
}
