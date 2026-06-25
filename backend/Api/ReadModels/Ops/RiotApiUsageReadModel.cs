namespace TrueMain.ReadModels.Ops;

/// <summary>
/// Riot API usage metrics over a relative window for the admin
/// <c>/ops/riot-usage</c> panel (#93): totals, a per-endpoint breakdown, a
/// status-code histogram, a bucketed call-volume series, and the most recent
/// rate-limit header snapshot. Sourced from the per-minute
/// <c>riot_api_call_rollups</c> Mongo collection.
/// </summary>
public sealed record RiotApiUsageReadModel
{
    /// <summary>The resolved window key echoed back: <c>1h</c> / <c>24h</c> / <c>7d</c>.</summary>
    public string Window { get; init; } = string.Empty;

    /// <summary>Lower time bound the metrics were aggregated from (UTC).</summary>
    public DateTime SinceUtc { get; init; }

    /// <summary>When the response was computed (UTC).</summary>
    public DateTime GeneratedAtUtc { get; init; }

    public long TotalCalls { get; init; }

    /// <summary>Count of non-2xx/3xx outcomes (transport faults, 429, 5xx).</summary>
    public long TotalErrors { get; init; }

    /// <summary>Errors / total calls in [0, 1]; 0 when there were no calls.</summary>
    public double ErrorRate { get; init; }

    /// <summary>Mean per-attempt round-trip latency in milliseconds.</summary>
    public double AvgLatencyMs { get; init; }

    public IReadOnlyList<RiotApiEndpointUsageReadModel> Endpoints { get; init; } = [];

    public IReadOnlyList<RiotApiStatusCountReadModel> StatusCodes { get; init; } = [];

    public IReadOnlyList<RiotApiUsageBucketReadModel> TimeSeries { get; init; } = [];

    /// <summary>Latest rate-limit header snapshot in the window, or null when none was seen.</summary>
    public RiotApiRateLimitReadModel? RateLimit { get; init; }
}

/// <summary>Per-endpoint rollup row (ordered by <see cref="Calls"/> desc).</summary>
public sealed record RiotApiEndpointUsageReadModel
{
    public string Endpoint { get; init; } = string.Empty;

    public long Calls { get; init; }

    public long Successes { get; init; }

    public long Errors { get; init; }

    public double AvgLatencyMs { get; init; }

    public DateTime LastCalledAtUtc { get; init; }
}

/// <summary>One status-code histogram row. <c>0</c> = transport fault (no response).</summary>
public sealed record RiotApiStatusCountReadModel
{
    public int StatusCode { get; init; }

    public long Count { get; init; }
}

/// <summary>One time bucket of the call-volume series (chronological).</summary>
public sealed record RiotApiUsageBucketReadModel
{
    public DateTime BucketUtc { get; init; }

    public long Calls { get; init; }

    public long Errors { get; init; }
}

/// <summary>
/// The most recent rate-limit header snapshot in the window. The app/method
/// counts are Riot's point-in-time <c>X-*-Rate-Limit[-Count]</c> header strings
/// (e.g. limit <c>20:1,100:120</c> / count <c>3:1,57:120</c>), surfaced verbatim
/// for the panel to parse and display.
/// </summary>
public sealed record RiotApiRateLimitReadModel
{
    public DateTime ObservedAtUtc { get; init; }

    public string? AppRateLimit { get; init; }

    public string? AppRateLimitCount { get; init; }

    public string? MethodRateLimit { get; init; }

    public string? MethodRateLimitCount { get; init; }

    public int? RetryAfterSeconds { get; init; }

    public string? RateLimitType { get; init; }
}
