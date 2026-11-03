using Data.Logging.Mongo;
using Data.Metrics.Mongo;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Ops;
using TrueMain.RequestLatency;

namespace TrueMain.Services.Ops.Diagnostics;

public interface IRequestLatencyQueryService
{
    /// <summary>
    /// Every ops route's latency over <paramref name="window"/> (<c>24h</c> or <c>7d</c>;
    /// anything else is 24h, like <c>/ops/riot-usage</c>), slowest p95 first.
    /// </summary>
    Task<RequestLatencyReadModel> GetAsync(string? window, CancellationToken ct);
}

/// <summary>
/// Reads the ops request-latency rollups (#2307). The store sums the minutes into bins per
/// route; the percentiles, the window totals and the full sparkline grid are made here.
/// </summary>
public sealed class RequestLatencyQueryService(
    IRequestLatencyStore store,
    RequestLatencyAccumulator accumulator,
    TimeProvider timeProvider,
    IOptions<MongoLoggingOptions> mongoOptions) : IRequestLatencyQueryService
{
    /// <summary>The p95 above which the panel calls a route slow: the epic's one-second target (#2300).</summary>
    internal const double SlowThresholdMs = 1_000;

    public async Task<RequestLatencyReadModel> GetAsync(string? window, CancellationToken ct)
    {
        var (key, span, binHours) = ResolveWindow(window);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var since = now - span;

        var rollups = await store.GetWindowAsync(since, binHours, ct);
        var retention = mongoOptions.Value.RequestLatencyRetention;

        return new RequestLatencyReadModel
        {
            Window = key,
            SinceUtc = since,
            GeneratedAtUtc = now,
            BinHours = binHours,
            SlowThresholdMs = SlowThresholdMs,
            RetentionDays = retention > TimeSpan.Zero ? retention.TotalDays : null,
            OldestRetainedUtc = rollups.OldestRetainedBucketUtc,
            DroppedSinceStart = accumulator.DroppedRequests,
            Routes = BuildRoutes(rollups.Bins, BinGrid(since, now, binHours))
        };
    }

    internal static (string Key, TimeSpan Span, int BinHours) ResolveWindow(string? window)
        => string.Equals(window?.Trim(), "7d", StringComparison.OrdinalIgnoreCase)
            ? ("7d", TimeSpan.FromDays(7), 6)
            : ("24h", TimeSpan.FromHours(24), 1);

    /// <summary>
    /// Every bin start from the one holding <paramref name="sinceUtc"/> to the one holding
    /// <paramref name="nowUtc"/>, aligned on UTC midnight as Mongo's <c>$dateTrunc</c> aligns them.
    /// </summary>
    internal static IReadOnlyList<DateTime> BinGrid(DateTime sinceUtc, DateTime nowUtc, int binHours)
    {
        var width = TimeSpan.FromHours(binHours).Ticks;
        var grid = new List<DateTime>();
        for (var ticks = sinceUtc.Ticks - (sinceUtc.Ticks % width); ticks <= nowUtc.Ticks; ticks += width)
        {
            grid.Add(new DateTime(ticks, DateTimeKind.Utc));
        }

        return grid;
    }

    internal static IReadOnlyList<RouteLatencyReadModel> BuildRoutes(
        IReadOnlyList<RequestLatencyBin> bins,
        IReadOnlyList<DateTime> grid)
        => bins
            .GroupBy(bin => (bin.Route, bin.Method))
            .Select(group => BuildRoute(group.Key.Route, group.Key.Method, group.ToList(), grid))
            .OrderByDescending(route => route.P95Ms)
            .ThenByDescending(route => route.Count)
            .ThenBy(route => route.Route, StringComparer.Ordinal)
            .ToList();

    private static RouteLatencyReadModel BuildRoute(
        string route,
        string method,
        IReadOnlyList<RequestLatencyBin> bins,
        IReadOnlyList<DateTime> grid)
    {
        var histogram = RequestLatencyHistogram.Sum(bins.Select(bin => bin.Histogram));
        var count = bins.Sum(bin => bin.Count);
        var maxMs = bins.Max(bin => bin.MaxMs);
        var byStart = bins.ToDictionary(bin => bin.BinStartUtc);

        return new RouteLatencyReadModel
        {
            Route = route,
            Method = method,
            Count = count,
            P50Ms = RequestLatencyHistogram.Percentile(histogram, 0.5, maxMs) ?? 0,
            P95Ms = RequestLatencyHistogram.Percentile(histogram, 0.95, maxMs) ?? 0,
            MeanMs = count > 0 ? bins.Sum(bin => bin.SumMs) / count : 0,
            MaxMs = maxMs,
            Timeouts = bins.Sum(bin => bin.Timeouts),
            ServerErrors = bins.Sum(bin => bin.ServerErrors),
            Series = grid
                .Select(start => byStart.TryGetValue(start, out var bin)
                    ? new RouteLatencyPointReadModel
                    {
                        BinStartUtc = start,
                        Count = bin.Count,
                        P95Ms = RequestLatencyHistogram.Percentile(bin.Histogram, 0.95, bin.MaxMs)
                    }
                    : new RouteLatencyPointReadModel { BinStartUtc = start })
                .ToList()
        };
    }
}
