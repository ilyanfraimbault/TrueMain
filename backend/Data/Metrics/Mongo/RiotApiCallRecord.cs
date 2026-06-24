namespace Data.Metrics.Mongo;

/// <summary>
/// Immutable snapshot of one Riot API request, captured by the Ingestor's
/// metrics handler and queued for the background <see cref="RiotApiMetricsSink"/>.
/// Kept as a plain record (not the <see cref="RiotApiCallRollupDocument"/>) so
/// producing it on the HTTP hot path never touches the Mongo driver; the sink
/// folds these into per-minute rollups.
/// </summary>
public sealed record RiotApiCallRecord(
    DateTime TimestampUtc,
    string Endpoint,
    string Method,
    int StatusCode,
    long LatencyMs,
    string? Route,
    string? AppRateLimit,
    string? AppRateLimitCount,
    string? MethodRateLimit,
    string? MethodRateLimitCount,
    int? RetryAfterSeconds,
    string? RateLimitType);
