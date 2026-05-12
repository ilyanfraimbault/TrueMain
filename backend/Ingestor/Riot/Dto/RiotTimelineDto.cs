using System.Text.Json.Serialization;

namespace Ingestor.Riot.Dto;

public class RiotTimelineDto
{
    [JsonPropertyName("info")]
    public RiotTimelineInfoDto Info { get; set; } = new();
}

public class RiotTimelineInfoDto
{
    [JsonPropertyName("frames")]
    public List<RiotTimelineFrameDto> Frames { get; set; } = new();
}

public class RiotTimelineFrameDto
{
    [JsonPropertyName("events")]
    public List<RiotTimelineEventDto> Events { get; set; } = new();
}

public class RiotTimelineEventDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("participantId")]
    public int? ParticipantId { get; set; }

    [JsonPropertyName("itemId")]
    public int? ItemId { get; set; }

    [JsonPropertyName("beforeId")]
    public int? BeforeId { get; set; }

    [JsonPropertyName("afterId")]
    public int? AfterId { get; set; }

    [JsonPropertyName("skillSlot")]
    public int? SkillSlot { get; set; }

    [JsonPropertyName("levelUpType")]
    public string? LevelUpType { get; set; }
}
