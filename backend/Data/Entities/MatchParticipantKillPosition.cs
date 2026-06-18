namespace Data.Entities;

/// <summary>
/// The map position of one kill participation (a kill or assist) by a participant
/// before the early-game cutoff, sourced from the Riot timeline. Deliberately
/// bounded — only kill participations, only before the cutoff — so the table stays
/// small (≈ tens of rows per match, not per-frame millions). Feeds the roam
/// metric (issue #536): kill participations outside the player's own lane.
/// </summary>
public class MatchParticipantKillPosition
{
    public Guid Id { get; set; }

    public string MatchId { get; set; } = string.Empty;

    public int ParticipantId { get; set; }

    public int TimestampMs { get; set; }

    public int X { get; set; }

    public int Y { get; set; }
}
