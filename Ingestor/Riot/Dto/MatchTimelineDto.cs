namespace Ingestor.Riot.Dto;

public class MatchTimelineDto
{
    public List<MatchTimelineEventDto> Events { get; set; } = new();
}

public class MatchTimelineEventDto
{
    public int ParticipantId { get; set; }

    public int TimestampMs { get; set; }

    public string Type { get; set; } = string.Empty;

    public int? ItemId { get; set; }

    public int? BeforeId { get; set; }

    public int? AfterId { get; set; }

    public int? SkillSlot { get; set; }

    public string? LevelUpType { get; set; }
}
