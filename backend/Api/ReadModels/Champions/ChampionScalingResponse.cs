namespace TrueMain.ReadModels.Champions;

/// <summary>
/// How a champion's win rate changes with game length, at a position. Win rate
/// is bucketed by game duration; <see cref="ScalingIndex"/> is the win-rate gap
/// between the longest and shortest qualifying bucket (positive = scales into
/// the late game). Computed live from match participants — no timeline needed.
/// </summary>
public sealed record ChampionScalingResponse
{
    public int ChampionId { get; init; }
    public string Position { get; init; } = string.Empty;
    public string? Patch { get; init; }
    public IReadOnlyList<ChampionScalingBucket> Buckets { get; init; } = [];
    public double? ScalingIndex { get; init; }
}

public sealed record ChampionScalingBucket
{
    /// <summary>Duration bucket index, 0 (shortest) to 4 (longest).</summary>
    public int Bucket { get; init; }
    public string Label { get; init; } = string.Empty;
    public int Games { get; init; }
    public double WinRate { get; init; }
}
