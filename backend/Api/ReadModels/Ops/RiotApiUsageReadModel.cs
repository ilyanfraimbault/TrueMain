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

    /// <summary>Calls attributed to each caller process, ordered by <c>Calls</c> descending (#1035).</summary>
    public IReadOnlyList<RiotApiCallerUsageReadModel> CallerBreakdown { get; init; } = [];

    /// <summary>Budget-headroom estimate, always computed over the last 7 days (#1035).</summary>
    public RiotApiHeadroomReadModel Headroom { get; init; } = new();
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

    /// <summary>Freshest <c>X-Method-Rate-Limit</c> header seen for this endpoint, or null (#1035).</summary>
    public string? MethodRateLimit { get; init; }

    /// <summary>Freshest <c>X-Method-Rate-Limit-Count</c> header seen for this endpoint, or null (#1035).</summary>
    public string? MethodRateLimitCount { get; init; }
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

    /// <summary>Subset of <see cref="Calls"/> that landed a 429 (#1035).</summary>
    public long Retries { get; init; }
}

/// <summary>Calls attributed to one caller process (#1035). <c>"unknown"</c> when unattributed.</summary>
public sealed record RiotApiCallerUsageReadModel
{
    public string Caller { get; init; } = string.Empty;

    public long Calls { get; init; }

    public long Errors { get; init; }
}

/// <summary>
/// Budget-headroom estimate (#1035): "how many more tracked accounts fit". Always
/// computed over the last 7 days, independent of the panel's selected window.
/// <see cref="SufficientData"/> is <see langword="false"/> — with only
/// <see cref="ObservedWindowHours"/>/<see cref="RequiredWindowHours"/> set — when
/// there isn't yet enough rollup history, no accounts are tracked, or no rate-limit
/// snapshot was seen; the estimate deliberately renders that absent state instead
/// of extrapolating from a too-thin window.
/// </summary>
public sealed record RiotApiHeadroomReadModel
{
    public bool SufficientData { get; init; }

    public double ObservedWindowHours { get; init; }

    public double RequiredWindowHours { get; init; }

    public long TrackedAccounts { get; init; }

    public double? CallsPerAccountPerDay { get; init; }

    public double? ObservedCallsPerDay { get; init; }

    public RiotApiBindingLimitReadModel? BindingLimit { get; init; }

    public double? SpareCallsPerDay { get; init; }

    /// <summary>Floor of spare capacity divided by the per-account cost.</summary>
    public long? AdditionalAccountsHeadroom { get; init; }
}

/// <summary>
/// The app rate-limit window with the smallest sustained-load daily ceiling among
/// the ones Riot returned (#1035) — the one that binds first under sustained
/// traffic, not necessarily the one with the highest current-instant usage ratio.
/// </summary>
public sealed record RiotApiBindingLimitReadModel
{
    public long Limit { get; init; }

    public int WindowSeconds { get; init; }

    public double MaxCallsPerDay { get; init; }
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
