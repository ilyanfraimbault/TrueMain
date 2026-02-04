namespace Data.Entities;

public class ParticipantSkillEvent
{
    public Guid Id { get; set; }

    public Guid MatchParticipantId { get; set; }

    public MatchParticipant MatchParticipant { get; set; } = null!;

    public string MatchId { get; set; } = string.Empty;

    public int ParticipantId { get; set; }

    public string Puuid { get; set; } = string.Empty;

    public int TimestampMs { get; set; }

    public int SkillSlot { get; set; }

    public string LevelUpType { get; set; } = string.Empty;
}
