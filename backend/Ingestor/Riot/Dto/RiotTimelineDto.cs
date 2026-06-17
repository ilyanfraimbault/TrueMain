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
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("events")]
    public List<RiotTimelineEventDto> Events { get; set; } = new();

    [JsonPropertyName("participantFrames")]
    public Dictionary<string, RiotTimelineParticipantFrameDto> ParticipantFrames { get; set; } = new();
}

public class RiotTimelineParticipantFrameDto
{
    [JsonPropertyName("participantId")]
    public int ParticipantId { get; set; }

    [JsonPropertyName("position")]
    public RiotTimelinePositionDto? Position { get; set; }

    [JsonPropertyName("currentGold")]
    public int CurrentGold { get; set; }

    [JsonPropertyName("totalGold")]
    public int TotalGold { get; set; }

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("xp")]
    public int Xp { get; set; }

    [JsonPropertyName("minionsKilled")]
    public int MinionsKilled { get; set; }

    [JsonPropertyName("jungleMinionsKilled")]
    public int JungleMinionsKilled { get; set; }

    [JsonPropertyName("damageStats")]
    public RiotTimelineDamageStatsDto? DamageStats { get; set; }
}

public class RiotTimelinePositionDto
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }
}

public class RiotTimelineDamageStatsDto
{
    [JsonPropertyName("totalDamageDoneToChampions")]
    public int TotalDamageDoneToChampions { get; set; }

    [JsonPropertyName("magicDamageDoneToChampions")]
    public int MagicDamageDoneToChampions { get; set; }

    [JsonPropertyName("physicalDamageDoneToChampions")]
    public int PhysicalDamageDoneToChampions { get; set; }

    [JsonPropertyName("trueDamageDoneToChampions")]
    public int TrueDamageDoneToChampions { get; set; }
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

    [JsonPropertyName("killerId")]
    public int? KillerId { get; set; }

    [JsonPropertyName("victimId")]
    public int? VictimId { get; set; }

    [JsonPropertyName("creatorId")]
    public int? CreatorId { get; set; }

    [JsonPropertyName("assistingParticipantIds")]
    public List<int>? AssistingParticipantIds { get; set; }

    [JsonPropertyName("position")]
    public RiotTimelinePositionDto? Position { get; set; }
}
