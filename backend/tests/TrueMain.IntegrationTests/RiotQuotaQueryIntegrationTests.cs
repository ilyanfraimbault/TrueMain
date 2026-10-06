using AwesomeAssertions;
using Data.Entities;
using Data.Logging.Mongo;
using Data.Metrics.Mongo;
using Data.Ops.Mongo;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The per-host quota read (#1458) and the process-run interval read behind the lane duty
/// cycle, against a real Mongo: the two-stage per-minute group, the per-route consumers,
/// the freshest headers per host and the run-end rule for still-running runs all execute
/// server-side.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RiotQuotaQueryIntegrationTests : IAsyncLifetime
{
    private readonly MongoFixture _mongo;

    public RiotQuotaQueryIntegrationTests(MongoFixture mongo)
    {
        _mongo = mongo;
    }

    public async ValueTask InitializeAsync() => await _mongo.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetAsync_SplitsCallsPerRoute_WithActiveMinutesConsumersAndFreshestHeaders()
    {
        var minute = TruncateToMinute(DateTime.UtcNow.AddMinutes(-10));
        await SeedAsync(
            // Two rollups in the same europe minute: one active minute, not two.
            Rollup(minute, "europe", "MatchIngestion", "match-v5.match", 200, 40, appCount: "2:1,60:120"),
            Rollup(minute, "europe", "MatchIngestion", "match-v5.timeline", 429, 3),
            Rollup(minute.AddMinutes(-5), "europe", "AccountRefresh", "account-v1.byPuuid", 200, 7, appCount: "1:1,10:120"),
            Rollup(minute, "asia", "MatchIngestion", "match-v5.match", 200, 90, appCount: "5:1,95:120"),
            Rollup(minute, "euw1", "Discovery", "summoner-v4.byPuuid", 503, 2),
            // No route, no caller: a pre-#1035 rollup must still land under "unknown".
            Rollup(minute, route: null, caller: null, "match-v5.match", 200, 1),
            // A legacy rollup (route = last seen in its minute): never counted per host.
            Rollup(minute.AddMinutes(-2), "asia", "MatchIngestion", "match-v5.match", 200, 1000, routeKeyed: false),
            // Outside the 1-hour window.
            Rollup(minute.AddHours(-3), "europe", "MatchIngestion", "match-v5.match", 200, 500, routeKeyed: false),
            Rollup(minute.AddHours(-2), "europe", "MatchIngestion", "match-v5.match", 200, 500));

        using var context = BuildContext();
        var usage = await new RiotQuotaQuery(context).GetAsync(RiotUsageWindow.LastHour, CancellationToken.None);

        usage.OldestRetainedBucketUtc.Should().Be(minute.AddHours(-3));
        usage.OldestRouteKeyedBucketUtc.Should().Be(minute.AddHours(-2));
        usage.Routes.Select(r => r.Route).Should().Equal("asia", "europe", "euw1", "unknown");

        var europe = usage.Routes.Single(r => r.Route == "europe");
        europe.Calls.Should().Be(50);
        europe.RateLimited.Should().Be(3);
        europe.Errors.Should().Be(3);
        europe.ActiveMinutes.Should().Be(2);
        // The freshest rollup carrying headers for this host, not the older one.
        europe.AppRateLimitCount.Should().Be("2:1,60:120");
        europe.AppRateLimit.Should().Be("20:1,100:120");
        europe.Consumers.Select(c => (c.Caller, c.Endpoint, c.Calls, c.RateLimited)).Should().Equal(
            ("MatchIngestion", "match-v5.match", 40L, 0L),
            ("AccountRefresh", "account-v1.byPuuid", 7L, 0L),
            ("MatchIngestion", "match-v5.timeline", 3L, 3L));

        var asia = usage.Routes.Single(r => r.Route == "asia");
        asia.Calls.Should().Be(90);
        asia.AppRateLimitCount.Should().Be("5:1,95:120");

        var euw1 = usage.Routes.Single(r => r.Route == "euw1");
        euw1.Errors.Should().Be(2);
        euw1.AppRateLimitCount.Should().BeNull();

        var unknown = usage.Routes.Single(r => r.Route == "unknown");
        unknown.Consumers.Single().Caller.Should().Be("unknown");
    }

    [Fact]
    public async Task GetAsync_EmptyCollection_ReturnsNoRoutesAndNoOldestBucket()
    {
        using var context = BuildContext();
        var usage = await new RiotQuotaQuery(context).GetAsync(RiotUsageWindow.Last30Days, CancellationToken.None);

        usage.Routes.Should().BeEmpty();
        usage.OldestRetainedBucketUtc.Should().BeNull();
        usage.OldestRouteKeyedBucketUtc.Should().BeNull();
        usage.SinceUtc.Should().BeCloseTo(DateTime.UtcNow.AddDays(-30), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ProcessRunIntervals_ReturnRunsOverlappingTheWindow_EndingRunningOnesAtTheirHeartbeat()
    {
        var now = DateTime.UtcNow;
        var since = now.AddHours(-1);
        var runs = _mongo.GetCollection<ProcessRunDocument>(MongoFixture.ProcessRunsCollection);
        await runs.InsertManyAsync(
        [
            Run("MatchIngestion", "FetchLane", now.AddMinutes(-30), now.AddMinutes(-10), ProcessRunStatus.Success),
            // Started before the window, still inside the look-behind.
            Run("Discovery", "FetchLane", now.AddMinutes(-70), now.AddMinutes(-50), ProcessRunStatus.Success),
            // Still running: its finish equals its start, its heartbeat is the real end.
            Run("Harvest", "FetchLane", now.AddMinutes(-5), now.AddMinutes(-5), ProcessRunStatus.Running,
                heartbeat: now.AddMinutes(-1)),
            // Beyond the look-behind: excluded.
            Run("Scoring", null, now.AddDays(-3), now.AddDays(-3).AddMinutes(5), ProcessRunStatus.Success)
        ]);

        using var context = BuildContext();
        var intervals = await new ProcessRunIntervalQuery(context).GetAsync(since, now, CancellationToken.None);

        intervals.Select(i => i.ProcessName).Should().BeEquivalentTo(["MatchIngestion", "Discovery", "Harvest"]);
        var harvest = intervals.Single(i => i.ProcessName == "Harvest");
        harvest.EndUtc.Should().BeCloseTo(now.AddMinutes(-1), TimeSpan.FromMilliseconds(5));
        harvest.Lane.Should().Be("FetchLane");
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
            ProcessRunsCollection = MongoFixture.ProcessRunsCollection,
            Enabled = true
        }));

    private static DateTime TruncateToMinute(DateTime at)
        => new(at.Year, at.Month, at.Day, at.Hour, at.Minute, 0, DateTimeKind.Utc);

    private static RiotApiCallRollupDocument Rollup(
        DateTime bucket,
        string? route,
        string? caller,
        string endpoint,
        int statusCode,
        long count,
        string? appCount = null,
        bool routeKeyed = true)
        => new()
        {
            RouteKeyed = routeKeyed,
            BucketStartUtc = bucket,
            Endpoint = endpoint,
            StatusCode = statusCode,
            Count = count,
            SumLatencyMs = count * 100,
            LastCalledAtUtc = bucket.AddSeconds(30),
            Route = route,
            CallerProcess = caller,
            AppRateLimit = appCount is null ? null : "20:1,100:120",
            AppRateLimitCount = appCount
        };

    private static ProcessRunDocument Run(
        string processName,
        string? lane,
        DateTime startedAt,
        DateTime finishedAt,
        ProcessRunStatus status,
        DateTime? heartbeat = null)
        => new()
        {
            Id = Guid.NewGuid(),
            ProcessName = processName,
            JobMode = lane,
            StartedAtUtc = startedAt,
            FinishedAtUtc = finishedAt,
            DurationMs = (int)(finishedAt - startedAt).TotalMilliseconds,
            Status = status,
            LastHeartbeatAtUtc = heartbeat
        };
}
