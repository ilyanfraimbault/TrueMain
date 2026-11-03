using AwesomeAssertions;
using Data.Metrics.Mongo;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TrueMain.RequestLatency;
using TrueMain.Services.Ops.Diagnostics;

namespace TrueMain.UnitTests;

/// <summary>
/// #2307: the ops request-latency measurement — the fixed histogram and the percentiles read
/// back from it, the bounded per-minute fold, what the middleware counts as an ops route, a
/// timeout or a 5xx, and how the query orders routes and fills the sparkline grid.
/// </summary>
public sealed class RequestLatencyTests
{
    private static readonly DateTime Minute = new(2026, 10, 6, 12, 34, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 0)]
    [InlineData(10.5, 1)]
    [InlineData(1_000, 7)]
    [InlineData(30_000, 12)]
    [InlineData(30_001, 13)]
    public void BucketOf_PlacesADurationInTheFirstBucketWhoseUpperBoundHoldsIt(double durationMs, int bucket)
        => RequestLatencyHistogram.BucketOf(durationMs).Should().Be(bucket);

    [Fact]
    public void Percentile_InterpolatesInsideTheBucketTheRankFallsIn()
    {
        // 100 requests in (100, 250] ms: the median sits halfway through that bucket.
        var buckets = Histogram((175, 100));

        RequestLatencyHistogram.Percentile(buckets, 0.5, maxMs: 250).Should().BeApproximately(175, 0.001);
        RequestLatencyHistogram.Percentile(buckets, 0.95, maxMs: 250).Should().BeApproximately(242.5, 0.001);
    }

    [Fact]
    public void Percentile_IsCappedAtTheSlowestRequestMeasured()
    {
        // One request in the open last bucket: its upper edge is the max, never infinity.
        var buckets = Histogram((20, 19), (45_000, 1));

        RequestLatencyHistogram.Percentile(buckets, 0.95, maxMs: 45_000).Should().BeLessThanOrEqualTo(45_000);
        RequestLatencyHistogram.Percentile(buckets, 1, maxMs: 45_000).Should().Be(45_000);
        RequestLatencyHistogram.Percentile(Histogram((20, 5)), 0.95, maxMs: 12).Should().BeLessThanOrEqualTo(12);
    }

    [Fact]
    public void Percentile_SeparatesTheTailFromTheBody()
    {
        var buckets = Histogram((40, 90), (6_000, 10));

        RequestLatencyHistogram.Percentile(buckets, 0.5, maxMs: 6_000).Should().BeLessThanOrEqualTo(50);
        RequestLatencyHistogram.Percentile(buckets, 0.95, maxMs: 6_000).Should().BeGreaterThan(5_000);
    }

    [Fact]
    public void Percentile_OfNothingCounted_IsNull()
        => RequestLatencyHistogram.Percentile(new long[RequestLatencyHistogram.BucketCount], 0.95, 0).Should().BeNull();

    [Fact]
    public void Sum_AddsHistogramsBucketByBucket()
    {
        var total = RequestLatencyHistogram.Sum([Histogram((40, 2)), Histogram((40, 1), (900, 3)), [1]]);

        total.Should().HaveCount(RequestLatencyHistogram.BucketCount);
        total[RequestLatencyHistogram.BucketOf(40)].Should().Be(3);
        total[RequestLatencyHistogram.BucketOf(900)].Should().Be(3);
        total[0].Should().Be(1);
    }

    [Fact]
    public void Accumulator_FoldsRequestsPerMinuteRouteAndMethod()
    {
        var accumulator = new RequestLatencyAccumulator();

        accumulator.Record(Sample("/ops/logs", Minute.AddSeconds(5), 40));
        accumulator.Record(Sample("/ops/logs", Minute.AddSeconds(50), 1_200, timedOut: true));
        accumulator.Record(Sample("/ops/logs", Minute.AddSeconds(70), 30, serverError: true));
        accumulator.Record(Sample("/ops/logs", Minute.AddSeconds(10), 30, method: "POST"));

        var rollups = accumulator.Drain();

        rollups.Should().HaveCount(3);
        var first = rollups.Single(rollup => rollup.BucketStartUtc == Minute && rollup.Method == "GET");
        first.Count.Should().Be(2);
        first.SumMs.Should().Be(1_240);
        first.MaxMs.Should().Be(1_200);
        first.Timeouts.Should().Be(1);
        first.ServerErrors.Should().Be(0);
        first.Histogram.Sum().Should().Be(2);
        first.LastRecordedAtUtc.Should().Be(Minute.AddSeconds(50));
        rollups.Single(rollup => rollup.BucketStartUtc == Minute.AddMinutes(1)).ServerErrors.Should().Be(1);
        accumulator.Drain().Should().BeEmpty("a drain hands the minutes over");
    }

    [Fact]
    public void Accumulator_DropsAndCountsNewKeysPastItsBound()
    {
        var accumulator = new RequestLatencyAccumulator();
        for (var index = 0; index < RequestLatencyAccumulator.MaxPendingKeys; index++)
        {
            accumulator.Record(Sample($"/ops/route{index}", Minute, 10));
        }

        accumulator.Record(Sample("/ops/one-too-many", Minute, 10));
        accumulator.Record(Sample("/ops/route0", Minute, 10));

        accumulator.DroppedRequests.Should().Be(1, "an existing key still folds, a new one is dropped");
        var rollups = accumulator.Drain();
        rollups.Should().HaveCount(RequestLatencyAccumulator.MaxPendingKeys);

        accumulator.CountDropped(rollups.Take(2));
        accumulator.DroppedRequests.Should().Be(4);
    }

    [Fact]
    public async Task Flusher_WritesWhatIsPending_AndCountsAFailedWriteAsDropped()
    {
        var accumulator = new RequestLatencyAccumulator();
        var store = Substitute.For<IRequestLatencyStore>();
        store.WriteAsync(Arg.Any<IReadOnlyCollection<RequestLatencyRollup>>(), Arg.Any<CancellationToken>())
            .Returns(1);
        using var flusher = new RequestLatencyFlusher(accumulator, store, NullLogger<RequestLatencyFlusher>.Instance);

        (await flusher.FlushAsync(CancellationToken.None)).Should().Be(0, "nothing pending, nothing written");
        accumulator.Record(Sample("/ops/logs", Minute, 10));
        (await flusher.FlushAsync(CancellationToken.None)).Should().Be(1);

        store.WriteAsync(Arg.Any<IReadOnlyCollection<RequestLatencyRollup>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("mongo down"));
        accumulator.Record(Sample("/ops/logs", Minute, 10));
        accumulator.Record(Sample("/ops/logs", Minute, 20));
        (await flusher.FlushAsync(CancellationToken.None)).Should().Be(0);
        accumulator.DroppedRequests.Should().Be(2);
    }

    [Theory]
    [InlineData("ops/logs", "/ops/logs")]
    [InlineData("/ops/accounts/{puuid}", "/ops/accounts/{puuid}")]
    [InlineData("champions/{id}", null)]
    [InlineData("opsy", null)]
    public void OpsRouteOf_KeepsOnlyTheOpsTemplates(string template, string? expected)
    {
        var endpoint = new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(template), 0, null, null);

        RequestLatencyMiddleware.OpsRouteOf(endpoint).Should().Be(expected);
    }

    [Fact]
    public void OpsRouteOf_WithoutARouteEndpoint_IsNull()
        => RequestLatencyMiddleware.OpsRouteOf(null).Should().BeNull();

    [Theory]
    [InlineData(200, false, false, false, false, false)]
    [InlineData(503, false, false, false, false, true)]
    [InlineData(504, false, false, false, true, false)]
    [InlineData(200, true, false, false, false, true)]
    [InlineData(200, false, true, false, true, false)]
    [InlineData(200, false, false, true, true, false)]
    public void Classify_CountsTimeoutsAndServerErrorsOnce(
        int status, bool threw, bool cancelled, bool aborted, bool timedOut, bool serverError)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Response.StatusCode = status;
        using var abort = new CancellationTokenSource();
        if (aborted)
        {
            abort.Cancel();
        }

        context.RequestAborted = abort.Token;

        var sample = RequestLatencyMiddleware.Classify(context, "/ops/logs", Minute, 12, threw, cancelled);

        sample.TimedOut.Should().Be(timedOut);
        sample.ServerError.Should().Be(serverError);
        sample.Route.Should().Be("/ops/logs");
        sample.DurationMs.Should().Be(12);
    }

    [Theory]
    [InlineData(null, "24h", 24, 1)]
    [InlineData("7D", "7d", 168, 6)]
    [InlineData("30d", "24h", 24, 1)]
    public void ResolveWindow_OffersTwentyFourHoursAndSevenDays(string? raw, string key, int hours, int binHours)
    {
        var (resolvedKey, span, resolvedBin) = RequestLatencyQueryService.ResolveWindow(raw);

        resolvedKey.Should().Be(key);
        span.Should().Be(TimeSpan.FromHours(hours));
        resolvedBin.Should().Be(binHours);
    }

    [Fact]
    public void BinGrid_CoversTheWindowOnUtcAlignedBins()
    {
        var grid = RequestLatencyQueryService.BinGrid(Minute.AddDays(-7), Minute, binHours: 6);

        grid[0].Should().Be(new DateTime(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc));
        grid[^1].Should().Be(new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc));
        grid.Should().HaveCount(29);
    }

    [Fact]
    public void BuildRoutes_SortsByP95_AndFillsEveryBinOfTheGrid()
    {
        var grid = RequestLatencyQueryService.BinGrid(Minute.AddHours(-2), Minute, binHours: 1);
        var bins = new List<RequestLatencyBin>
        {
            Bin("/ops/logs", grid[0], (30, 10)),
            Bin("/ops/logs", grid[2], (40, 10)),
            Bin("/ops/stats/overview", grid[1], (3_000, 4))
        };

        var routes = RequestLatencyQueryService.BuildRoutes(bins, grid);

        routes.Select(route => route.Route).Should().Equal("/ops/stats/overview", "/ops/logs");
        var logs = routes[1];
        logs.Count.Should().Be(20);
        logs.MeanMs.Should().Be(35);
        logs.MaxMs.Should().Be(40);
        logs.Series.Should().HaveCount(3);
        logs.Series[1].Count.Should().Be(0);
        logs.Series[1].P95Ms.Should().BeNull("an idle hour is a gap, not a zero");
        logs.Series[2].P95Ms.Should().NotBeNull();
        routes[0].P95Ms.Should().BeGreaterThan(1_000);
    }

    private static long[] Histogram(params (double DurationMs, long Count)[] entries)
    {
        var buckets = new long[RequestLatencyHistogram.BucketCount];
        foreach (var (duration, count) in entries)
        {
            buckets[RequestLatencyHistogram.BucketOf(duration)] += count;
        }

        return buckets;
    }

    private static RequestLatencyBin Bin(string route, DateTime start, (double DurationMs, long Count) entry)
        => new(route, "GET", start, entry.Count, entry.DurationMs * entry.Count, entry.DurationMs, Histogram(entry), 0, 0);

    private static RequestLatencySample Sample(
        string route,
        DateTime startedAtUtc,
        double durationMs,
        bool timedOut = false,
        bool serverError = false,
        string method = "GET")
        => new(route, method, startedAtUtc, durationMs, timedOut, serverError);
}
