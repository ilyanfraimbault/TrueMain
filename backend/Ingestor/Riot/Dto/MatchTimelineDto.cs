namespace Ingestor.Riot.Dto;

public class MatchTimelineDto
{
    public List<MatchTimelineEventDto> Events { get; set; } = new();

    public List<MatchTimelineFrameDto> Frames { get; set; } = new();
}

public class MatchTimelineFrameDto
{
    public int TimestampMs { get; set; }

    public List<MatchParticipantFrameDto> ParticipantFrames { get; set; } = new();
}

public class MatchParticipantFrameDto
{
    public int ParticipantId { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int CurrentGold { get; set; }

    public int TotalGold { get; set; }

    public int Level { get; set; }

    public int Xp { get; set; }

    public int MinionsKilled { get; set; }

    public int JungleMinionsKilled { get; set; }

    public int TotalDamageToChampions { get; set; }
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

    public int? KillerId { get; set; }

    public int? VictimId { get; set; }

    public IReadOnlyList<int> AssistingParticipantIds { get; set; } = [];

    public int? PositionX { get; set; }

    public int? PositionY { get; set; }
}
