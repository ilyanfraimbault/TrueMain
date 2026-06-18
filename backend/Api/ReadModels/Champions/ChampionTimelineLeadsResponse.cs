namespace TrueMain.ReadModels.Champions;

/// <summary>
/// A champion's average lead vs its lane opponent at each minute mark
/// (5/10/15/20/30), aggregated from per-interval timeline snapshots. Positive
/// diffs mean the champion is ahead of the opposing lane at that interval.
/// </summary>
public sealed record ChampionTimelineLeadsResponse
{
    public int ChampionId { get; init; }
    public string Position { get; init; } = string.Empty;
    public string? Patch { get; init; }
    public IReadOnlyList<ChampionTimelineLeadEntry> Intervals { get; init; } = [];
}

public sealed record ChampionTimelineLeadEntry
{
    public int IntervalMinute { get; init; }
    public int Games { get; init; }
    public double GoldDiff { get; init; }
    public double CsDiff { get; init; }
    public double KillsDiff { get; init; }
    public double LevelDiff { get; init; }
    public double XpDiff { get; init; }
    public double DamageDiff { get; init; }
}
