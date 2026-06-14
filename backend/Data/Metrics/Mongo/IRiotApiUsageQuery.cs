namespace Data.Metrics.Mongo;

/// <summary>
/// Relative time window the <c>/ops/riot-usage</c> panel can request. Each window
/// fixes both the lower time bound and the time-series bucket size so the chart
/// stays readable (roughly 12–28 buckets per window).
/// </summary>
public enum RiotUsageWindow
{
    /// <summary>Last hour, bucketed in 5-minute steps.</summary>
    LastHour,

    /// <summary>Last 24 hours, bucketed hourly.</summary>
    Last24Hours,

    /// <summary>Last 7 days, bucketed in 6-hour steps.</summary>
    Last7Days
}

/// <summary>
/// Read query over the <c>riot_api_calls</c> collection backing the admin
/// <c>/ops/riot-usage</c> panel (#93). Lives in the Data layer so the Api stays
/// persistence-ignorant and consumes the <see cref="RiotApiUsage"/> read-model.
/// </summary>
public interface IRiotApiUsageQuery
{
    /// <summary>
    /// Aggregates Riot API call metrics over the given <paramref name="window"/>:
    /// totals, a per-endpoint breakdown, a status-code breakdown, a bucketed
    /// time-series, and the most recent rate-limit header snapshot.
    /// </summary>
    /// <remarks>
    /// The per-endpoint breakdown is unbounded by design but bounded in practice:
    /// keys come from <c>RiotEndpointClassifier</c>, a fixed finite set, and every
    /// unrecognised path collapses to the single <c>"unknown"</c> key — so the
    /// group never grows with traffic and needs no <c>$limit</c>.
    /// <para>
    /// The rate-limit snapshot is <em>window-scoped only</em>: <paramref name="endpoint"/>
    /// restricts the breakdowns but not the snapshot, because
    /// <c>X-App-Rate-Limit[-Count]</c> is app-wide rather than per endpoint.
    /// </para>
    /// </remarks>
    /// <param name="window">Relative window (also fixes the bucket size).</param>
    /// <param name="endpoint">Optional exact endpoint key to restrict the breakdowns to.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<RiotApiUsage> GetAsync(RiotUsageWindow window, string? endpoint, CancellationToken ct);
}

/// <summary>
/// Aggregated Riot API usage over a window. <see cref="TotalErrors"/> counts every
/// non-2xx/3xx outcome (status 0 transport faults, 429s, 5xx). Latency is the mean
/// per-attempt round trip in milliseconds.
/// </summary>
public sealed record RiotApiUsage(
    DateTime SinceUtc,
    long TotalCalls,
    long TotalErrors,
    double AvgLatencyMs,
    IReadOnlyList<RiotApiEndpointUsage> Endpoints,
    IReadOnlyList<RiotApiStatusCount> StatusCodes,
    IReadOnlyList<RiotApiUsageBucket> TimeSeries,
    RiotApiRateLimitSnapshot? RateLimit);

/// <summary>Per-endpoint rollup, ordered by <see cref="Calls"/> descending.</summary>
public sealed record RiotApiEndpointUsage(
    string Endpoint,
    long Calls,
    long Successes,
    long Errors,
    double AvgLatencyMs,
    DateTime LastCalledAtUtc);

/// <summary>One row of the status-code histogram. <c>0</c> means a transport fault (no response).</summary>
public sealed record RiotApiStatusCount(int StatusCode, long Count);

/// <summary>One time bucket of the call-volume series (chronological).</summary>
public sealed record RiotApiUsageBucket(DateTime BucketUtc, long Calls, long Errors);

/// <summary>
/// The most recent Riot rate-limit header snapshot in the window, or null when no
/// call carried rate-limit headers. The app/method counts are point-in-time
/// values Riot returns on every response, so the latest call reflects "current"
/// consumption.
/// </summary>
public sealed record RiotApiRateLimitSnapshot(
    DateTime ObservedAtUtc,
    string? AppRateLimit,
    string? AppRateLimitCount,
    string? MethodRateLimit,
    string? MethodRateLimitCount,
    int? RetryAfterSeconds,
    string? RateLimitType);
