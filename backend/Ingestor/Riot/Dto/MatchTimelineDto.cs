namespace Ingestor.Riot.Dto;

public sealed record MatchTimelineDto
{
    public IReadOnlyList<MatchTimelineEventDto> Events { get; init; } = [];

    public IReadOnlyList<MatchTimelineFrameDto> Frames { get; init; } = [];
}

public sealed record MatchTimelineFrameDto
{
    public int TimestampMs { get; init; }

    public IReadOnlyList<MatchParticipantFrameDto> ParticipantFrames { get; init; } = [];
}

public sealed record MatchParticipantFrameDto
{
    public int ParticipantId { get; init; }

    // Null when the frame carries no position, kept distinct from a real (0, 0)
    // coordinate so pathing/heatmap consumers (#535) can drop GPS-less frames.
    public int? X { get; init; }

    public int? Y { get; init; }

    public int CurrentGold { get; init; }

    public int TotalGold { get; init; }

    public int Level { get; init; }

    public int Xp { get; init; }

    public int MinionsKilled { get; init; }

    public int JungleMinionsKilled { get; init; }

    // Only the total is propagated. The Riot payload also splits magic/physical/true
    // damage to champions (deserialized in RiotTimelineDamageStatsDto) — intentionally
    // not mapped here (YAGNI); add them if a downstream analytic needs the breakdown.
    public int TotalDamageToChampions { get; init; }
}

public sealed record MatchTimelineEventDto
{
    public int ParticipantId { get; init; }

    public int TimestampMs { get; init; }

    public string Type { get; init; } = string.Empty;

    public int? ItemId { get; init; }

    public int? BeforeId { get; init; }

    public int? AfterId { get; init; }

    public int? SkillSlot { get; init; }

    public string? LevelUpType { get; init; }

    public int? KillerId { get; init; }

    public int? VictimId { get; init; }

    public int? CreatorId { get; init; }

    public IReadOnlyList<int> AssistingParticipantIds { get; init; } = [];

    public int? PositionX { get; init; }

    public int? PositionY { get; init; }
}
