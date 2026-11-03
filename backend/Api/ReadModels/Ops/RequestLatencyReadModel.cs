namespace TrueMain.ReadModels.Ops;

/// <summary>
/// How long the Api took to answer each ops route over a window (#2307), for the admin
/// Processes → Performance tab. Measured server-side, from the request reaching the Api to its
/// answer; the admin proxy's own hop is not in it.
/// </summary>
public sealed record RequestLatencyReadModel
{
    /// <summary>The resolved window key: <c>24h</c> or <c>7d</c>.</summary>
    public string Window { get; init; } = string.Empty;

    public DateTime SinceUtc { get; init; }

    public DateTime GeneratedAtUtc { get; init; }

    /// <summary>The width of one sparkline point, in hours.</summary>
    public int BinHours { get; init; }

    /// <summary>A route whose p95 is above this is slow; the panel highlights it.</summary>
    public double SlowThresholdMs { get; init; }

    /// <summary>The rollup retention the Api is configured with, in days; null when the TTL is disabled.</summary>
    public double? RetentionDays { get; init; }

    /// <summary>
    /// The oldest rollup still stored; null when none is, meaning nothing was ever recorded —
    /// not a quiet window. A window reaching further back was only partly measured.
    /// </summary>
    public DateTime? OldestRetainedUtc { get; init; }

    /// <summary>
    /// Requests this Api process measured but could not persist since it started (a failed
    /// write, or more pending routes than the accumulator holds). Zero on a healthy process.
    /// </summary>
    public long DroppedSinceStart { get; init; }

    /// <summary>Every ops route answered in the window, slowest p95 first.</summary>
    public IReadOnlyList<RouteLatencyReadModel> Routes { get; init; } = [];
}

/// <summary>One route over the window.</summary>
public sealed record RouteLatencyReadModel
{
    /// <summary>The route template, e.g. <c>/ops/accounts/{puuid}</c>.</summary>
    public string Route { get; init; } = string.Empty;

    public string Method { get; init; } = string.Empty;

    public long Count { get; init; }

    /// <summary>Median duration, estimated from the bucket counts.</summary>
    public double P50Ms { get; init; }

    /// <summary>95th percentile, estimated from the bucket counts; never above <see cref="MaxMs"/>.</summary>
    public double P95Ms { get; init; }

    public double MeanMs { get; init; }

    /// <summary>The slowest request actually measured.</summary>
    public double MaxMs { get; init; }

    /// <summary>Requests abandoned by the client, cancelled or timed out.</summary>
    public long Timeouts { get; init; }

    /// <summary>Requests answered with a 5xx.</summary>
    public long ServerErrors { get; init; }

    /// <summary>One point per <see cref="RequestLatencyReadModel.BinHours"/> across the whole window, oldest first.</summary>
    public IReadOnlyList<RouteLatencyPointReadModel> Series { get; init; } = [];
}

/// <summary>One sparkline point; <see cref="P95Ms"/> is null when the route was not called in the bin.</summary>
public sealed record RouteLatencyPointReadModel
{
    public DateTime BinStartUtc { get; init; }

    public long Count { get; init; }

    public double? P95Ms { get; init; }
}
