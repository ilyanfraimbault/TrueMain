namespace Data.Metrics.Mongo;

/// <summary>
/// Read query over the <c>riot_api_call_rollups</c> collection for the per-routing-host
/// quota view of the admin Riot API panel (#1458). Riot enforces the app rate limit per
/// routing host, so the budget is one pool per host rather than one overall: this query
/// returns each host's measured volume, its rate-limit pressure, who spent it, and the
/// freshest rate-limit headers that host returned. The utilisation arithmetic on top of
/// these counts lives in the Api, like the budget-headroom estimate.
/// </summary>
public interface IRiotQuotaQuery
{
    /// <summary>
    /// Aggregates the rollups of <paramref name="window"/> per routing host. Never
    /// endpoint-filtered: a host's budget is shared by every endpoint it serves.
    /// </summary>
    Task<RiotQuotaUsage> GetAsync(RiotUsageWindow window, CancellationToken ct);

    /// <summary>
    /// Raw inputs for the budget-headroom estimate (#1035, #1894): each host's calls and
    /// freshest advertised limit over the last 7 days, whatever window the operator selected,
    /// and never endpoint-filtered — "how many more accounts fit" must not depend on what the
    /// page is looking at. Route-keyed rollups only, like <see cref="GetAsync"/>.
    /// </summary>
    Task<RiotHeadroomInputs> GetHeadroomInputsAsync(CancellationToken ct);
}

/// <summary>
/// Per-host Riot API consumption over a window, counted from route-keyed rollups only
/// (<see cref="RiotApiCallRollupDocument.RouteKeyed"/>). <see cref="OldestRetainedBucketUtc"/>
/// is the oldest rollup left in the collection, <see cref="OldestRouteKeyedBucketUtc"/>
/// the oldest one counted here (each null when there is none): when either is later than
/// <see cref="SinceUtc"/>, the retention or the cutover — not the traffic — cut the
/// window short, and a rate computed over the full window would understate it.
/// </summary>
public sealed record RiotQuotaUsage(
    DateTime SinceUtc,
    DateTime? OldestRetainedBucketUtc,
    DateTime? OldestRouteKeyedBucketUtc,
    IReadOnlyList<RiotRouteUsage> Routes);

/// <summary>
/// The last 7 days of per-host volume behind the budget-headroom estimate (#1894).
/// <see cref="OldestRouteKeyedBucketUtc"/> is the oldest route-keyed rollup in the collection
/// (null when there is none): later than <see cref="SinceUtc"/>, it means the per-host history
/// is shorter than the window and a daily rate must be measured over the shorter span.
/// </summary>
public sealed record RiotHeadroomInputs(
    DateTime SinceUtc,
    DateTime? OldestRouteKeyedBucketUtc,
    IReadOnlyList<RiotRouteVolume> Routes);

/// <summary>
/// One routing host's calls over the headroom window, every attempt counted (429s included),
/// with the freshest <c>X-App-Rate-Limit</c> header it returned — null when none carried one.
/// <see cref="Route"/> is <c>"unknown"</c> for calls whose host could not be read.
/// </summary>
public sealed record RiotRouteVolume(string Route, long Calls, string? AppRateLimit);

/// <summary>
/// One routing host's consumption, ordered by <see cref="Calls"/> descending.
/// <see cref="Route"/> is <c>"unknown"</c> for calls whose host could not be read.
/// <see cref="Calls"/> counts every physical attempt — a 429 is a request the host
/// still counted. <see cref="ActiveMinutes"/> is the number of distinct minutes with at
/// least one call to this host. <see cref="AppRateLimit"/> /
/// <see cref="AppRateLimitCount"/> are the freshest <c>X-App-Rate-Limit[-Count]</c>
/// headers this host returned in the window, observed at <see cref="ObservedAtUtc"/>.
/// </summary>
public sealed record RiotRouteUsage(
    string Route,
    long Calls,
    long RateLimited,
    long Errors,
    long ActiveMinutes,
    string? AppRateLimit,
    string? AppRateLimitCount,
    DateTime? ObservedAtUtc,
    IReadOnlyList<RiotRouteConsumer> Consumers);

/// <summary>
/// Calls one caller process made to one endpoint on a host, ordered by
/// <see cref="Calls"/> descending. <see cref="Caller"/> is <c>"unknown"</c> for calls
/// made outside a tracked pipeline pass.
/// </summary>
public sealed record RiotRouteConsumer(string Caller, string Endpoint, long Calls, long RateLimited);
