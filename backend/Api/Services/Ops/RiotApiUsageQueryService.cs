using Data.Metrics.Mongo;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Reads Riot API usage metrics for the admin <c>/ops/riot-usage</c> panel (#93).
/// Thin adapter over the Data-layer <see cref="IRiotApiUsageQuery"/>: the Mongo
/// aggregation and window semantics live in Data (so the Api stays
/// persistence-ignorant); this service parses the window string and maps the
/// <see cref="RiotApiUsage"/> read-model onto the <see cref="RiotApiUsageReadModel"/>
/// API contract.
/// </summary>
public sealed class RiotApiUsageQueryService(IRiotApiUsageQuery query) : IRiotApiUsageQueryService
{
    public async Task<RiotApiUsageReadModel> GetAsync(string? window, string? endpoint, CancellationToken ct)
    {
        var (resolved, key) = ResolveWindow(window);
        var usage = await query.GetAsync(resolved, endpoint, ct);

        return new RiotApiUsageReadModel
        {
            Window = key,
            SinceUtc = usage.SinceUtc,
            GeneratedAtUtc = DateTime.UtcNow,
            TotalCalls = usage.TotalCalls,
            TotalErrors = usage.TotalErrors,
            ErrorRate = usage.TotalCalls > 0 ? (double)usage.TotalErrors / usage.TotalCalls : 0,
            AvgLatencyMs = usage.AvgLatencyMs,
            Endpoints = usage.Endpoints
                .Select(e => new RiotApiEndpointUsageReadModel
                {
                    Endpoint = e.Endpoint,
                    Calls = e.Calls,
                    Successes = e.Successes,
                    Errors = e.Errors,
                    AvgLatencyMs = e.AvgLatencyMs,
                    LastCalledAtUtc = e.LastCalledAtUtc
                })
                .ToList(),
            StatusCodes = usage.StatusCodes
                .Select(s => new RiotApiStatusCountReadModel
                {
                    StatusCode = s.StatusCode,
                    Count = s.Count
                })
                .ToList(),
            TimeSeries = usage.TimeSeries
                .Select(b => new RiotApiUsageBucketReadModel
                {
                    BucketUtc = b.BucketUtc,
                    Calls = b.Calls,
                    Errors = b.Errors
                })
                .ToList(),
            RateLimit = usage.RateLimit is null
                ? null
                : new RiotApiRateLimitReadModel
                {
                    ObservedAtUtc = usage.RateLimit.ObservedAtUtc,
                    AppRateLimit = usage.RateLimit.AppRateLimit,
                    AppRateLimitCount = usage.RateLimit.AppRateLimitCount,
                    MethodRateLimit = usage.RateLimit.MethodRateLimit,
                    MethodRateLimitCount = usage.RateLimit.MethodRateLimitCount,
                    RetryAfterSeconds = usage.RateLimit.RetryAfterSeconds,
                    RateLimitType = usage.RateLimit.RateLimitType
                }
        };
    }

    /// <summary>
    /// Maps the query-string window (<c>1h</c> / <c>24h</c> / <c>7d</c>) to the
    /// Data window enum and the canonical key echoed back. Unknown/blank values
    /// default to 24h so a malformed param degrades gracefully.
    /// </summary>
    private static (RiotUsageWindow Window, string Key) ResolveWindow(string? window)
        => (window?.Trim().ToLowerInvariant()) switch
        {
            "1h" => (RiotUsageWindow.LastHour, "1h"),
            "7d" => (RiotUsageWindow.Last7Days, "7d"),
            _ => (RiotUsageWindow.Last24Hours, "24h")
        };
}
