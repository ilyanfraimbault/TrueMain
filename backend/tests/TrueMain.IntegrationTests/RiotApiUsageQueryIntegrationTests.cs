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

    private async Task<RiotApiUsage> QueryAsync(RiotUsageWindow window, string? endpoint = null)
    {
        // A fresh context per call mirrors the DI singleton's lifetime; dispose it
        // so the owned IMongoClient's pool is torn down like the container would.
        using var context = BuildContext();
        var query = new RiotApiUsageQuery(context);
        return await query.GetAsync(window, endpoint, CancellationToken.None);
    }

    private async Task SeedAsync(params RiotApiCallDocument[] documents)
    {
        var collection = _mongo.GetCollection<RiotApiCallDocument>(MongoFixture.RiotApiCallsCollection);
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

    private static RiotApiCallDocument Call(
        string endpoint,
        int statusCode,
        long latencyMs,
        int minutesAgo,
        string? appLimit = null,
        string? appCount = null)
        => new()
        {
            TimestampUtc = DateTime.UtcNow.AddMinutes(-minutesAgo),
            Endpoint = endpoint,
            Method = "GET",
            StatusCode = statusCode,
            LatencyMs = latencyMs,
            AppRateLimit = appLimit,
            AppRateLimitCount = appCount
        };
}
