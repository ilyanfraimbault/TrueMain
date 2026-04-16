namespace TrueMain.Contracts.Ops;

public sealed class PipelineHealthResponse
{
    public IReadOnlyList<ProcessHealthResponse> Processes { get; init; } = [];

    public RawDataFreshnessResponse RawData { get; init; } = new();

    public PipelineGapResponse Gaps { get; init; } = new();
}

public sealed class ProcessHealthResponse
{
    public string ProcessName { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTime LastStartedAtUtc { get; init; }

    public DateTime LastFinishedAtUtc { get; init; }

    public int DurationMs { get; init; }

    public string? Error { get; init; }
}

public sealed class RawDataFreshnessResponse
{
    public int QueueId { get; init; }

    public int RawMatchCount { get; init; }

    public int RawParticipantCount { get; init; }

    public IReadOnlyList<PlatformRawDataFreshnessResponse> Platforms { get; init; } = [];
}

public sealed class PlatformRawDataFreshnessResponse
{
    public string PlatformId { get; init; } = string.Empty;

    public DateTime? LatestMatchStartAtUtc { get; init; }

    public string LatestPatchVersion { get; init; } = string.Empty;
}

public sealed class PipelineGapResponse
{
    public double? MatchIngestionToMainAnalysisMinutes { get; init; }

    public double? ChampionDataLagMinutes { get; init; }
}
