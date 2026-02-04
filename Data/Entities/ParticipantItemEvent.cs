namespace Data.Entities;

public class ParticipantItemEvent
{
    public Guid Id { get; set; }

    public Guid MatchParticipantId { get; set; }

    public MatchParticipant MatchParticipant { get; set; } = null!;

    public string MatchId { get; set; } = string.Empty;

    public int ParticipantId { get; set; }

    public string Puuid { get; set; } = string.Empty;

    public int TimestampMs { get; set; }

    public string EventType { get; set; } = string.Empty;

    public int ItemId { get; set; }

    public int? BeforeId { get; set; }

    public int? AfterId { get; set; }
}
