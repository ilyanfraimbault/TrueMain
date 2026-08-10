using System.Globalization;
using Data;
using Data.Metrics.Mongo;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Reads Riot API usage metrics for the admin <c>/ops/riot-usage</c> panel (#93).
/// Thin adapter over the Data-layer <see cref="IRiotApiUsageQuery"/>: the Mongo
/// aggregation and window semantics live in Data (so the Api stays
/// persistence-ignorant); this service parses the window string and maps the
/// <see cref="RiotApiUsage"/> read-model onto the <see cref="RiotApiUsageReadModel"/>
/// API contract. It also composes the budget-headroom estimate (#1035): Mongo call
/// volume plus the Postgres tracked-account count (same read
/// <see cref="OverviewQueryService"/> uses) is arithmetic, not persistence, so it
/// lives here rather than in either data layer.
/// </summary>
public sealed class RiotApiUsageQueryService(IRiotApiUsageQuery query, TrueMainDbContext db)
    : IRiotApiUsageQueryService
{
    // Below this much observed history, a calls/day extrapolation is treated as
    // unreliable rather than guessed at — the estimate renders an explicit absent
    // state instead (#1035 acceptance criterion).
    private static readonly TimeSpan MinimumSaturationWindow = TimeSpan.FromHours(24);

    public async Task<RiotApiUsageReadModel> GetAsync(string? window, string? endpoint, CancellationToken ct)
    {
        var (resolved, key) = ResolveWindow(window);

        var usageTask = query.GetAsync(resolved, endpoint, ct);
        var saturationTask = query.GetSaturationInputsAsync(ct);
        var trackedAccountsTask = db.RiotAccounts.AsNoTracking().CountAsync(ct);

        await Task.WhenAll(usageTask, saturationTask, trackedAccountsTask);

        var usage = await usageTask;
        var saturation = await saturationTask;
        var trackedAccounts = await trackedAccountsTask;

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
                    LastCalledAtUtc = e.LastCalledAtUtc,
                    MethodRateLimit = e.MethodRateLimit,
                    MethodRateLimitCount = e.MethodRateLimitCount
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
                    Errors = b.Errors,
                    Retries = b.Retries
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
                },
            CallerBreakdown = usage.CallerBreakdown
                .Select(c => new RiotApiCallerUsageReadModel
                {
                    Caller = c.Caller,
                    Calls = c.Calls,
                    Errors = c.Errors
                })
                .ToList(),
            Headroom = BuildHeadroom(saturation, trackedAccounts)
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

    /// <summary>
    /// Arithmetic on measured cost per account (#1035): calls/day observed over
    /// the last 7 days, divided by tracked accounts, compared against the
    /// tightest sustained-load app rate-limit ceiling. Guards against three ways
    /// the estimate could mislead rather than inform: too little history to trust
    /// the daily rate, zero tracked accounts (division has no meaning), and no
    /// rate-limit snapshot to compare against.
    /// </summary>
    internal static RiotApiHeadroomReadModel BuildHeadroom(RiotApiSaturationInputs saturation, int trackedAccounts)
    {
        var now = DateTime.UtcNow;
        var observedSpan = now - (saturation.EarliestBucketUtc ?? now);
        var requiredHours = MinimumSaturationWindow.TotalHours;

        if (observedSpan < MinimumSaturationWindow || trackedAccounts <= 0 || saturation.RateLimit is null)
        {
            return new RiotApiHeadroomReadModel
            {
                SufficientData = false,
                ObservedWindowHours = observedSpan.TotalHours,
                RequiredWindowHours = requiredHours,
                TrackedAccounts = Math.Max(0, trackedAccounts)
            };
        }

        var bindingLimit = ResolveBindingLimit(saturation.RateLimit.AppRateLimit);
        if (bindingLimit is null)
        {
            return new RiotApiHeadroomReadModel
            {
                SufficientData = false,
                ObservedWindowHours = observedSpan.TotalHours,
                RequiredWindowHours = requiredHours,
                TrackedAccounts = trackedAccounts
            };
        }

        var observedCallsPerDay = saturation.TotalCalls / observedSpan.TotalDays;
        var callsPerAccountPerDay = observedCallsPerDay / trackedAccounts;
        var spareCallsPerDay = Math.Max(0, bindingLimit.MaxCallsPerDay - observedCallsPerDay);
        var additionalAccounts = callsPerAccountPerDay > 0
            ? (long)Math.Floor(spareCallsPerDay / callsPerAccountPerDay)
            : (long?)null;

        return new RiotApiHeadroomReadModel
        {
            SufficientData = true,
            ObservedWindowHours = observedSpan.TotalHours,
            RequiredWindowHours = requiredHours,
            TrackedAccounts = trackedAccounts,
            ObservedCallsPerDay = observedCallsPerDay,
            CallsPerAccountPerDay = callsPerAccountPerDay,
            BindingLimit = bindingLimit,
            SpareCallsPerDay = spareCallsPerDay,
            AdditionalAccountsHeadroom = additionalAccounts
        };
    }

    /// <summary>
    /// Parses Riot's <c>X-App-Rate-Limit</c> pairs (<c>"20:1,100:120"</c> →
    /// limit:windowSeconds) and picks the one with the smallest sustained-load
    /// daily ceiling (<c>limit * 86400 / windowSeconds</c>) — the window that
    /// binds first under sustained traffic, independent of which one currently
    /// shows the highest instant usage ratio. Null when the header is absent or
    /// every pair is malformed.
    /// </summary>
    internal static RiotApiBindingLimitReadModel? ResolveBindingLimit(string? appRateLimit)
    {
        if (string.IsNullOrWhiteSpace(appRateLimit))
        {
            return null;
        }

        RiotApiBindingLimitReadModel? binding = null;
        foreach (var pair in appRateLimit.Split(','))
        {
            var parts = pair.Split(':');
            if (parts.Length != 2
                || !long.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit)
                || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var windowSeconds)
                || windowSeconds <= 0)
            {
                continue;
            }

            var maxCallsPerDay = limit * 86_400.0 / windowSeconds;
            if (binding is null || maxCallsPerDay < binding.MaxCallsPerDay)
            {
                binding = new RiotApiBindingLimitReadModel
                {
                    Limit = limit,
                    WindowSeconds = windowSeconds,
                    MaxCallsPerDay = maxCallsPerDay
                };
            }
        }

        return binding;
    }
}
