using System.Text.Json.Serialization;

namespace Ingestor.Riot.Dto;

public sealed record RiotTimelineDto
{
    [JsonPropertyName("info")]
    public RiotTimelineInfoDto Info { get; init; } = new();
}

public sealed record RiotTimelineInfoDto
{
    [JsonPropertyName("frames")]
    public IReadOnlyList<RiotTimelineFrameDto> Frames { get; init; } = [];
}

public sealed record RiotTimelineFrameDto
{
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    [JsonPropertyName("events")]
    public IReadOnlyList<RiotTimelineEventDto> Events { get; init; } = [];

    [JsonPropertyName("participantFrames")]
    public IReadOnlyDictionary<string, RiotTimelineParticipantFrameDto> ParticipantFrames { get; init; } = new Dictionary<string, RiotTimelineParticipantFrameDto>();
}

public sealed record RiotTimelineParticipantFrameDto
{
    [JsonPropertyName("participantId")]
    public int ParticipantId { get; init; }

    [JsonPropertyName("position")]
    public RiotTimelinePositionDto? Position { get; init; }

    [JsonPropertyName("currentGold")]
    public int CurrentGold { get; init; }

    [JsonPropertyName("totalGold")]
    public int TotalGold { get; init; }

    [JsonPropertyName("level")]
    public int Level { get; init; }

    [JsonPropertyName("xp")]
    public int Xp { get; init; }

    [JsonPropertyName("minionsKilled")]
    public int MinionsKilled { get; init; }

    [JsonPropertyName("jungleMinionsKilled")]
    public int JungleMinionsKilled { get; init; }

    [JsonPropertyName("damageStats")]
    public RiotTimelineDamageStatsDto? DamageStats { get; init; }
}

public sealed record RiotTimelinePositionDto
{
    [JsonPropertyName("x")]
    public int X { get; init; }

    [JsonPropertyName("y")]
    public int Y { get; init; }
}

public sealed record RiotTimelineDamageStatsDto
{
    [JsonPropertyName("totalDamageDoneToChampions")]
    public int TotalDamageDoneToChampions { get; init; }

    [JsonPropertyName("magicDamageDoneToChampions")]
    public int MagicDamageDoneToChampions { get; init; }

    [JsonPropertyName("physicalDamageDoneToChampions")]
    public int PhysicalDamageDoneToChampions { get; init; }

    [JsonPropertyName("trueDamageDoneToChampions")]
    public int TrueDamageDoneToChampions { get; init; }
}

public sealed record RiotTimelineEventDto
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    [JsonPropertyName("participantId")]
    public int? ParticipantId { get; init; }

    [JsonPropertyName("itemId")]
    public int? ItemId { get; init; }

    [JsonPropertyName("beforeId")]
    public int? BeforeId { get; init; }

    [JsonPropertyName("afterId")]
    public int? AfterId { get; init; }

    [JsonPropertyName("skillSlot")]
    public int? SkillSlot { get; init; }

    [JsonPropertyName("levelUpType")]
    public string? LevelUpType { get; init; }

    [JsonPropertyName("killerId")]
    public int? KillerId { get; init; }

    [JsonPropertyName("victimId")]
    public int? VictimId { get; init; }

    [JsonPropertyName("creatorId")]
    public int? CreatorId { get; init; }

    [JsonPropertyName("assistingParticipantIds")]
    public IReadOnlyList<int>? AssistingParticipantIds { get; init; }

    [JsonPropertyName("position")]
    public RiotTimelinePositionDto? Position { get; init; }
}
