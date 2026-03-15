namespace TrueMain.ReadModels.Ops;

public sealed class PipelineHealthReadModel
{
    public IReadOnlyList<ProcessHealthReadModel> Processes { get; init; } = [];

    public RawDataFreshnessReadModel RawData { get; init; } = new();

    public PipelineGapReadModel Gaps { get; init; } = new();
}

public sealed class ProcessHealthReadModel
{
    public string ProcessName { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTime LastStartedAtUtc { get; init; }

    public DateTime LastFinishedAtUtc { get; init; }

    public int DurationMs { get; init; }

    public string? Error { get; init; }
}

public sealed class RawDataFreshnessReadModel
{
    public int QueueId { get; init; }

    public int RawMatchCount { get; init; }

    public int RawParticipantCount { get; init; }

    public IReadOnlyList<PlatformRawDataFreshnessReadModel> Platforms { get; init; } = [];
}

public sealed class PlatformRawDataFreshnessReadModel
{
    public string PlatformId { get; init; } = string.Empty;

    public DateTime? LatestMatchStartAtUtc { get; init; }

    public string LatestPatchVersion { get; init; } = string.Empty;
}

public sealed class PipelineGapReadModel
{
    public double? MatchIngestionToMainAnalysisMinutes { get; init; }

    public double? ChampionDataLagMinutes { get; init; }
}
