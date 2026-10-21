using System.Globalization;
using Data;
using Data.Metrics.Mongo;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Health;

namespace TrueMain.Services.Ops.Stats;

public interface IRiotApiUsageQueryService
{
    /// <summary>
    /// Builds the Riot API usage read-model for the given relative
    /// <paramref name="window"/> (<c>1h</c> / <c>24h</c> / <c>7d</c> / <c>30d</c>; unknown or
    /// null defaults to 24h), optionally restricted to a single
    /// <paramref name="endpoint"/> key.
    /// </summary>
    Task<RiotApiUsageReadModel> GetAsync(string? window, string? endpoint, CancellationToken ct);
}

/// <summary>
/// Reads Riot API usage metrics for the admin <c>/ops/riot-usage</c> panel (#93).
/// Thin adapter over the Data-layer <see cref="IRiotApiUsageQuery"/>: the Mongo
/// aggregation and window semantics live in Data (so the Api stays
/// persistence-ignorant); this service parses the window string and maps the
/// <see cref="RiotApiUsage"/> read-model onto the <see cref="RiotApiUsageReadModel"/>
/// API contract. It also composes the budget-headroom estimate (#1035, per routing host
/// since #1894): per-host Mongo call volume plus the Postgres tracked-account count (same
/// read <see cref="OverviewQueryService"/> uses) is arithmetic, not persistence, so it
/// lives here rather than in either data layer.
/// </summary>
public sealed class RiotApiUsageQueryService(
    IRiotApiUsageQuery query,
    IRiotQuotaQuery quotaQuery,
    TrueMainDbContext db) : IRiotApiUsageQueryService
{
    // Below this much observed history, a calls/day extrapolation is treated as
    // unreliable rather than guessed at — the estimate renders an explicit absent
    // state instead (#1035 acceptance criterion).
    private static readonly TimeSpan MinimumHeadroomWindow = TimeSpan.FromHours(24);

    public async Task<RiotApiUsageReadModel> GetAsync(string? window, string? endpoint, CancellationToken ct)
    {
        var (resolved, key) = ResolveWindow(window);

        var usageTask = query.GetAsync(resolved, endpoint, ct);
        var headroomTask = quotaQuery.GetHeadroomInputsAsync(ct);
        var trackedAccountsTask = db.RiotAccounts.AsNoTracking().CountAsync(ct);

        await Task.WhenAll(usageTask, headroomTask, trackedAccountsTask);

        var usage = await usageTask;
        var headroom = await headroomTask;
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
            Headroom = BuildHeadroom(headroom, trackedAccounts, DateTime.UtcNow)
        };
    }

    /// <summary>
    /// Maps the query-string window (<c>1h</c> / <c>24h</c> / <c>7d</c> / <c>30d</c>) to
    /// the Data window enum and the canonical key echoed back. Unknown/blank values
    /// default to 24h so a malformed param degrades gracefully. Shared with
    /// <see cref="RiotQuotaQueryService"/>, whose panel sits under the same selector.
    /// </summary>
    internal static (RiotUsageWindow Window, string Key) ResolveWindow(string? window)
        => (window?.Trim().ToLowerInvariant()) switch
        {
            "1h" => (RiotUsageWindow.LastHour, "1h"),
            "7d" => (RiotUsageWindow.Last7Days, "7d"),
            "30d" => (RiotUsageWindow.Last30Days, "30d"),
            _ => (RiotUsageWindow.Last24Hours, "24h")
        };

    /// <summary>
    /// Arithmetic on measured cost per account (#1035), one routing host at a time (#1894):
    /// Riot meters each host separately, so a host's calls/day over the covered span are
    /// divided by tracked accounts and compared against the tightest sustained ceiling of
    /// <em>that host's</em> advertised app limit — calls from different hosts are never added,
    /// nor compared to another host's limit. The host with the fewest additional accounts
    /// binds: it caps the estimate. New accounts are assumed to cost each host what the
    /// current ones do (the current regional mix).
    /// <para>
    /// Three guards keep the estimate from misleading: route-keyed history shorter than
    /// <see cref="MinimumHeadroomWindow"/> (the retention or the #1458 cutover may cut the
    /// 7 days short, and the rate is measured over the covered span only), no tracked
    /// accounts, and no host with a parseable limit header. Calls whose host could not be
    /// read (<c>"unknown"</c>) belong to no budget and are left out.
    /// </para>
    /// </summary>
    internal static RiotApiHeadroomReadModel BuildHeadroom(
        RiotHeadroomInputs inputs,
        int trackedAccounts,
        DateTime now)
    {
        var counted = inputs.OldestRouteKeyedBucketUtc ?? now;
        var coverageStart = counted > inputs.SinceUtc ? counted : inputs.SinceUtc;
        var covered = now - coverageStart;
        var insufficient = new RiotApiHeadroomReadModel
        {
            SufficientData = false,
            ObservedWindowHours = Math.Max(0, covered.TotalHours),
            RequiredWindowHours = MinimumHeadroomWindow.TotalHours,
            TrackedAccounts = Math.Max(0, trackedAccounts)
        };

        if (covered < MinimumHeadroomWindow || trackedAccounts <= 0)
        {
            return insufficient;
        }

        var routes = inputs.Routes
            .Where(route => route.Route != RiotQuotaQueryService.UnknownRoute)
            .Select(route => BuildRouteHeadroom(route, covered, trackedAccounts))
            .OrderBy(route => route.AdditionalAccountsHeadroom ?? long.MaxValue)
            .ThenBy(route => route.Route, StringComparer.Ordinal)
            .ToList();

        if (routes.All(route => route.BindingLimit is null))
        {
            return insufficient;
        }

        var binding = routes.FirstOrDefault(route => route.AdditionalAccountsHeadroom is not null);

        return insufficient with
        {
            SufficientData = true,
            AdditionalAccountsHeadroom = binding?.AdditionalAccountsHeadroom,
            BindingRoute = binding?.Route,
            Routes = routes
        };
    }

    /// <summary>
    /// One host's share of the estimate. A host that advertised no parseable limit keeps its
    /// measured cost but gets no spare capacity nor account count — it cannot bind.
    /// </summary>
    internal static RiotRouteHeadroomReadModel BuildRouteHeadroom(
        RiotRouteVolume route,
        TimeSpan covered,
        int trackedAccounts)
    {
        var observedCallsPerDay = route.Calls / covered.TotalDays;
        var callsPerAccountPerDay = observedCallsPerDay / trackedAccounts;
        var bindingLimit = ResolveBindingLimit(route.AppRateLimit);
        var spareCallsPerDay = bindingLimit is null
            ? (double?)null
            : Math.Max(0, bindingLimit.MaxCallsPerDay - observedCallsPerDay);
        var additionalAccounts = spareCallsPerDay is not null && callsPerAccountPerDay > 0
            ? (long)Math.Floor(spareCallsPerDay.Value / callsPerAccountPerDay)
            : (long?)null;

        return new RiotRouteHeadroomReadModel
        {
            Route = route.Route,
            Kind = RiotQuotaQueryService.KindOf(route.Route),
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
