using System.Globalization;
using Data.Logging.Mongo;
using Data.Metrics.Mongo;
using Data.Ops.Mongo;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Stats;

public interface IRiotQuotaQueryService
{
    /// <summary>
    /// Per-host quota utilisation and per-lane duty cycle over the relative
    /// <paramref name="window"/> (<c>1h</c> / <c>24h</c> / <c>7d</c> / <c>30d</c>;
    /// anything else is 24h, like <c>/ops/riot-usage</c>).
    /// </summary>
    Task<RiotQuotaReadModel> GetAsync(string? window, CancellationToken ct);
}

/// <summary>
/// The arithmetic behind the admin quota view (#1458): measured per-host volume from the
/// rollups divided by the limit each host advertised, and the union of recorded process
/// runs per lane divided by the window. The Data queries only count; every ratio is made
/// here, from those counts and nothing else.
/// </summary>
public sealed class RiotQuotaQueryService(
    IRiotQuotaQuery quotaQuery,
    IProcessRunIntervalQuery runQuery,
    IOptions<MongoLoggingOptions> mongoOptions) : IRiotQuotaQueryService
{
    // The regional routing values: account-v1 and match-v5 live on these, every other
    // endpoint the ingestor calls lives on a platform host. Separate budgets.
    private static readonly HashSet<string> RegionalRoutes =
        new(["americas", "europe", "asia", "sea"], StringComparer.OrdinalIgnoreCase);

    public async Task<RiotQuotaReadModel> GetAsync(string? window, CancellationToken ct)
    {
        var (resolved, key) = RiotApiUsageQueryService.ResolveWindow(window);

        var usage = await quotaQuery.GetAsync(resolved, ct);
        var now = DateTime.UtcNow;
        var runs = await runQuery.GetAsync(usage.SinceUtc, now, ct);

        // The retention or the route-keyed cutover, not the traffic, may have cut the window
        // short: measure from whichever is later so a rate is never diluted by days that
        // were not counted. No route-keyed rollup at all means nothing was measured yet.
        var counted = usage.OldestRouteKeyedBucketUtc ?? now;
        var coverageStart = counted > usage.SinceUtc ? counted : usage.SinceUtc;
        var covered = now - coverageStart;
        var retention = mongoOptions.Value.RiotApiCallsRetention;

        return new RiotQuotaReadModel
        {
            Window = key,
            SinceUtc = usage.SinceUtc,
            GeneratedAtUtc = now,
            CoverageStartUtc = coverageStart,
            CoveredHours = covered.TotalHours,
            RetentionDays = retention > TimeSpan.Zero ? retention.TotalDays : null,
            OldestRetainedUtc = usage.OldestRetainedBucketUtc,
            Routes = usage.Routes
                .Select(route => BuildRoute(route, covered))
                .OrderBy(route => route.Kind == "regional" ? 0 : route.Kind == "platform" ? 1 : 2)
                .ThenByDescending(route => route.Calls)
                .ToList(),
            Lanes = BuildLanes(runs, usage.SinceUtc, now)
        };
    }

    internal static RiotRouteQuotaReadModel BuildRoute(RiotRouteUsage route, TimeSpan covered)
    {
        var binding = RiotApiUsageQueryService.ResolveBindingLimit(route.AppRateLimit);
        var coveredMinutes = Math.Max(1, covered.TotalMinutes);
        var allowed = binding is null ? 0 : binding.MaxCallsPerDay * covered.TotalDays;

        return new RiotRouteQuotaReadModel
        {
            Route = route.Route,
            Kind = RegionalRoutes.Contains(route.Route) ? "regional"
                : route.Route == "unknown" ? "unknown"
                : "platform",
            Calls = route.Calls,
            RateLimited = route.RateLimited,
            RateLimitedRate = route.Calls > 0 ? (double)route.RateLimited / route.Calls : null,
            Errors = route.Errors,
            CallsPerMinute = route.Calls / coveredMinutes,
            ActiveMinuteShare = Math.Min(1, route.ActiveMinutes / Math.Ceiling(coveredMinutes)),
            BindingLimit = binding,
            Utilisation = allowed > 0 ? route.Calls / allowed : null,
            CurrentUtilisation = binding is null
                ? null
                : CurrentFill(route.AppRateLimitCount, binding.WindowSeconds, binding.Limit),
            AppRateLimit = route.AppRateLimit,
            AppRateLimitCount = route.AppRateLimitCount,
            ObservedAtUtc = route.ObservedAtUtc,
            Consumers = route.Consumers
                .Select(consumer => new RiotRouteConsumerReadModel
                {
                    Caller = consumer.Caller,
                    Endpoint = consumer.Endpoint,
                    Calls = consumer.Calls,
                    RateLimited = consumer.RateLimited,
                    Share = route.Calls > 0 ? (double)consumer.Calls / route.Calls : 0
                })
                .ToList()
        };
    }

    /// <summary>
    /// The count Riot reported for <paramref name="windowSeconds"/> in an
    /// <c>X-App-Rate-Limit-Count</c> header (<c>"3:1,57:120"</c>), over
    /// <paramref name="limit"/>. Null when the header has no such window.
    /// </summary>
    internal static double? CurrentFill(string? appRateLimitCount, int windowSeconds, long limit)
    {
        if (string.IsNullOrWhiteSpace(appRateLimitCount) || limit <= 0)
        {
            return null;
        }

        foreach (var pair in appRateLimitCount.Split(','))
        {
            var parts = pair.Split(':');
            if (parts.Length == 2
                && long.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
                && int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                && seconds == windowSeconds)
            {
                return (double)count / limit;
            }
        }

        return null;
    }

    /// <summary>
    /// Duty cycle per lane and per process: each run clipped to <c>[since, now]</c>, then
    /// the union of the clipped intervals — not their sum, so two processes of one lane
    /// running at once are not counted twice and a lane never exceeds 100 %.
    /// </summary>
    internal static IReadOnlyList<LaneDutyCycleReadModel> BuildLanes(
        IReadOnlyList<ProcessRunInterval> runs,
        DateTime since,
        DateTime now)
    {
        var window = now - since;
        if (window <= TimeSpan.Zero)
        {
            return [];
        }

        var clipped = runs
            .Select(run => (Run: run, Start: Max(run.StartUtc, since), End: Min(run.EndUtc, now)))
            .Where(run => run.End > run.Start || (run.Run.StartUtc >= since && run.Run.StartUtc <= now))
            .ToList();

        return clipped
            .GroupBy(run => string.IsNullOrWhiteSpace(run.Run.Lane) ? "unassigned" : run.Run.Lane!)
            .Select(lane =>
            {
                var busy = UnionLength(lane.Select(run => (run.Start, run.End)));
                return new LaneDutyCycleReadModel
                {
                    Lane = lane.Key,
                    DutyCycle = busy / window,
                    BusyHours = busy.TotalHours,
                    Runs = lane.Count(),
                    Processes = lane
                        .GroupBy(run => run.Run.ProcessName)
                        .Select(process =>
                        {
                            var processBusy = UnionLength(process.Select(run => (run.Start, run.End)));
                            return new ProcessDutyCycleReadModel
                            {
                                ProcessName = process.Key,
                                DutyCycle = processBusy / window,
                                BusyHours = processBusy.TotalHours,
                                Runs = process.Count()
                            };
                        })
                        .OrderByDescending(process => process.DutyCycle)
                        .ThenBy(process => process.ProcessName, StringComparer.Ordinal)
                        .ToList()
                };
            })
            .OrderByDescending(lane => lane.DutyCycle)
            .ThenBy(lane => lane.Lane, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Total length covered by the intervals, overlaps counted once.</summary>
    internal static TimeSpan UnionLength(IEnumerable<(DateTime Start, DateTime End)> intervals)
    {
        var total = TimeSpan.Zero;
        DateTime? openStart = null;
        var openEnd = DateTime.MinValue;

        foreach (var (start, end) in intervals.Where(i => i.End > i.Start).OrderBy(i => i.Start))
        {
            if (openStart is null || start > openEnd)
            {
                if (openStart is not null)
                {
                    total += openEnd - openStart.Value;
                }

                openStart = start;
                openEnd = end;
            }
            else if (end > openEnd)
            {
                openEnd = end;
            }
        }

        if (openStart is not null)
        {
            total += openEnd - openStart.Value;
        }

        return total;
    }

    private static DateTime Max(DateTime left, DateTime right) => left >= right ? left : right;

    private static DateTime Min(DateTime left, DateTime right) => left <= right ? left : right;
}
