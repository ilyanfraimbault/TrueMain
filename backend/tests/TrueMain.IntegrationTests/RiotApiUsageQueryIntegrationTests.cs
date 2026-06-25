using AwesomeAssertions;
using Data.Logging.Mongo;
using Data.Metrics.Mongo;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Exercises the <see cref="RiotApiUsageQuery"/> aggregation against a real Mongo
/// container (the <c>$group</c> / <c>$dateTrunc</c> stages run server-side, so a
/// mocked context could not cover them): totals, the weighted-latency mean, the
/// per-endpoint and status-code breakdowns, the bucketed time-series, the latest
/// rate-limit snapshot, the endpoint filter, the window lower bound and the
/// empty-collection path (#93).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RiotApiUsageQueryIntegrationTests
{
    private readonly MongoFixture _mongo;

    public RiotApiUsageQueryIntegrationTests(MongoFixture mongo)
    {
        _mongo = mongo;
    }

    [Fact]
    public async Task GetAsync_AggregatesTotalsEndpointsStatusTimeSeriesAndRateLimit()
    {
        await _mongo.ResetAsync();
        await SeedAsync(
            Call("match-v5.match", 200, 120, minutesAgo: 5, appLimit: "20:1,100:120", appCount: "3:1,57:120"),
            Call("match-v5.match", 200, 80, minutesAgo: 8),
            Call("match-v5.timeline", 429, 30, minutesAgo: 12),
            Call("league-v4.challenger", 200, 300, minutesAgo: 20),
            Call("summoner-v4.byPuuid", 503, 1000, minutesAgo: 40),
            Call("match-v5.match", 0, 2000, minutesAgo: 50));

        var usage = await QueryAsync(RiotUsageWindow.LastHour);

        usage.TotalCalls.Should().Be(6);
        // status 0 (transport fault), 429 and 503 are errors; the three 200s succeed.
        usage.TotalErrors.Should().Be(3);
        // Weighted mean latency across all six attempts: 3530 / 6.
        usage.AvgLatencyMs.Should().BeApproximately(3530d / 6, 0.01);

        // Endpoints are ordered by call count desc; match-v5.match leads with 3.
        var top = usage.Endpoints[0];
        top.Endpoint.Should().Be("match-v5.match");
        top.Calls.Should().Be(3);
        top.Successes.Should().Be(2);
        top.Errors.Should().Be(1);
        top.AvgLatencyMs.Should().BeApproximately((120d + 80 + 2000) / 3, 0.01);

        // Status histogram covers every observed code, including the 0 fault.
        usage.StatusCodes.Should().BeEquivalentTo(new[]
        {
            new RiotApiStatusCount(0, 1),
            new RiotApiStatusCount(200, 3),
            new RiotApiStatusCount(429, 1),
            new RiotApiStatusCount(503, 1)
        });

        // The time-series buckets account for every call and stay chronological.
        usage.TimeSeries.Sum(bucket => bucket.Calls).Should().Be(6);
        usage.TimeSeries.Select(bucket => bucket.BucketUtc).Should().BeInAscendingOrder();

        // The latest call carrying rate-limit headers is the 5-minutes-ago one.
        usage.RateLimit.Should().NotBeNull();
        usage.RateLimit!.AppRateLimit.Should().Be("20:1,100:120");
        usage.RateLimit.AppRateLimitCount.Should().Be("3:1,57:120");
    }

    [Fact]
    public async Task GetAsync_WithEndpointFilter_RestrictsToThatEndpoint()
    {
        await _mongo.ResetAsync();
        await SeedAsync(
            Call("match-v5.match", 200, 100, minutesAgo: 3),
            Call("match-v5.match", 200, 100, minutesAgo: 4),
            // Most recent, different endpoint, carries the app rate-limit headers.
            Call("league-v4.challenger", 200, 100, minutesAgo: 1, appLimit: "20:1", appCount: "5:1"));

        var usage = await QueryAsync(RiotUsageWindow.LastHour, endpoint: "match-v5.match");

        // Breakdowns are restricted to the filtered endpoint...
        usage.TotalCalls.Should().Be(2);
        usage.Endpoints.Should().ContainSingle()
            .Which.Endpoint.Should().Be("match-v5.match");
        // ...but the app-wide rate-limit snapshot is window-scoped, so it still
        // surfaces the league call's headers despite the match-only filter.
        usage.RateLimit.Should().NotBeNull();
        usage.RateLimit!.AppRateLimitCount.Should().Be("5:1");
    }

    [Fact]
    public async Task GetAsync_ExcludesCallsOlderThanTheWindow()
    {
        await _mongo.ResetAsync();
        await SeedAsync(
            Call("match-v5.match", 200, 100, minutesAgo: 5),
            // Two hours old: outside the one-hour window.
            Call("match-v5.match", 200, 100, minutesAgo: 120));

        var usage = await QueryAsync(RiotUsageWindow.LastHour);

        usage.TotalCalls.Should().Be(1);
    }

    [Fact]
    public async Task GetAsync_EmptyCollection_ReturnsZeroedResult()
    {
        await _mongo.ResetAsync();

        var usage = await QueryAsync(RiotUsageWindow.Last24Hours);

        usage.TotalCalls.Should().Be(0);
        usage.TotalErrors.Should().Be(0);
        usage.AvgLatencyMs.Should().Be(0);
        usage.Endpoints.Should().BeEmpty();
        usage.StatusCodes.Should().BeEmpty();
        usage.TimeSeries.Should().BeEmpty();
        usage.RateLimit.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_SumsRollupCounts_NotDocumentCount()
    {
        await _mongo.ResetAsync();
        var at = DateTime.UtcNow.AddMinutes(-3);
        var bucket = new DateTime(at.Year, at.Month, at.Day, at.Hour, at.Minute, 0, DateTimeKind.Utc);
        // One rollup standing for 10 attempts (4 of them 429 errors), 5000ms total.
        await SeedAsync(new RiotApiCallRollupDocument
        {
            BucketStartUtc = bucket,
            Endpoint = "match-v5.match",
            StatusCode = 200,
            Count = 6,
            SumLatencyMs = 3000,
            LastCalledAtUtc = at
        });
        await SeedAsync(new RiotApiCallRollupDocument
        {
            BucketStartUtc = bucket,
            Endpoint = "match-v5.match",
            StatusCode = 429,
            Count = 4,
            SumLatencyMs = 2000,
            LastCalledAtUtc = at
        });

        var usage = await QueryAsync(RiotUsageWindow.LastHour);

        // Totals reflect the rolled-up counts (10 attempts), not the 2 documents.
        usage.TotalCalls.Should().Be(10);
        usage.TotalErrors.Should().Be(4);
        usage.AvgLatencyMs.Should().BeApproximately(5000d / 10, 0.01);

        var endpoint = usage.Endpoints.Should().ContainSingle().Subject;
        endpoint.Calls.Should().Be(10);
        endpoint.Successes.Should().Be(6);
        endpoint.Errors.Should().Be(4);

        usage.StatusCodes.Should().BeEquivalentTo(new[]
        {
            new RiotApiStatusCount(200, 6),
            new RiotApiStatusCount(429, 4)
        });
        usage.TimeSeries.Sum(b => b.Calls).Should().Be(10);
    }

    private async Task<RiotApiUsage> QueryAsync(RiotUsageWindow window, string? endpoint = null)
    {
        // A fresh context per call mirrors the DI singleton's lifetime; dispose it
        // so the owned IMongoClient's pool is torn down like the container would.
        using var context = BuildContext();
        var query = new RiotApiUsageQuery(context);
        return await query.GetAsync(window, endpoint, CancellationToken.None);
    }

    private async Task SeedAsync(params RiotApiCallRollupDocument[] documents)
    {
        var collection = _mongo.GetCollection<RiotApiCallRollupDocument>(MongoFixture.RiotApiCallsCollection);
        await collection.InsertManyAsync(documents);
    }

    private MongoLogContext BuildContext()
        => new(Microsoft.Extensions.Options.Options.Create(new MongoLoggingOptions
        {
            ConnectionString = _mongo.ConnectionString,
            Database = MongoFixture.DatabaseName,
            RiotApiCallsCollection = MongoFixture.RiotApiCallsCollection,
            Enabled = true
        }));

    // A single call expressed as a one-attempt rollup (Count = 1): each helper call
    // is in its own minute, so the query's per-endpoint / status / time-series
    // groups sum these the same way the sink's upserts would have folded them.
    private static RiotApiCallRollupDocument Call(
        string endpoint,
        int statusCode,
        long latencyMs,
        int minutesAgo,
        string? appLimit = null,
        string? appCount = null)
    {
        var at = DateTime.UtcNow.AddMinutes(-minutesAgo);
        var bucket = new DateTime(at.Year, at.Month, at.Day, at.Hour, at.Minute, 0, DateTimeKind.Utc);
        return new RiotApiCallRollupDocument
        {
            BucketStartUtc = bucket,
            Endpoint = endpoint,
            StatusCode = statusCode,
            Count = 1,
            SumLatencyMs = latencyMs,
            LastCalledAtUtc = at,
            Method = "GET",
            AppRateLimit = appLimit,
            AppRateLimitCount = appCount
        };
    }
}
