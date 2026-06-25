using AwesomeAssertions;
using Data.Logging.Mongo;
using Data.Metrics.Mongo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Driver;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Exercises the Riot metrics write path against a real Mongo container: the
/// <see cref="IRiotApiCallRecorder"/> enqueues onto the channel and the hosted
/// <see cref="RiotApiMetricsSink"/> drains it, creates the collection indexes on
/// startup and folds the records into per-minute rollups (calls sharing a
/// minute/endpoint/status collapse into one <c>$inc</c>-upserted document).
/// Mirrors the diagnostic-sink integration test (the codebase tests sinks against
/// Mongo rather than with mocked collections) (#93).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class RiotApiMetricsSinkIntegrationTests
{
    private readonly MongoFixture _mongo;

    public RiotApiMetricsSinkIntegrationTests(MongoFixture mongo)
    {
        _mongo = mongo;
    }

    [Fact]
    public async Task Sink_FoldsRecordedCalls_IntoPerMinuteRollupsAndCreatesIndexes()
    {
        await _mongo.ResetAsync();

        using var host = BuildHost();
        await host.StartAsync();

        var collection = _mongo.GetCollection<RiotApiCallRollupDocument>(MongoFixture.RiotApiCallsCollection);

        // The sink creates its indexes once on startup; wait for the supporting
        // indexes (including the unique upsert key) so the assertion below is not
        // racing startup.
        await WaitUntilAsync(async () =>
        {
            var names = (await collection.Indexes.List().ToListAsync())
                .Select(index => index["name"].AsString)
                .ToList();
            return names.Contains("ux_bucket_endpoint_status")
                && names.Contains("ix_timestamp_desc")
                && names.Contains("ix_endpoint_timestamp")
                && names.Contains("ttl_timestamp");
        });

        // Pin a single minute so the two match calls share a bucket and must fold
        // into one rollup (count 2) rather than two documents.
        var at = DateTime.UtcNow;
        var recorder = host.Services.GetRequiredService<IRiotApiCallRecorder>();
        recorder.Record(Record("match-v5.match", 200, 120, at, appLimit: "20:1,100:120", appCount: "1:1,40:120"));
        recorder.Record(Record("match-v5.match", 200, 80, at, appLimit: "20:1,100:120", appCount: "3:1,57:120"));
        recorder.Record(Record("match-v5.timeline", 429, 30, at, retryAfter: 5));
        recorder.Record(Record("summoner-v4.byPuuid", 0, 1000, at));

        // The sink flushes on its short window; poll until the two match calls have
        // folded into a single count-2 rollup and all three rollups are present.
        await WaitUntilAsync(async () =>
        {
            var docs = await collection.Find(FilterDefinition<RiotApiCallRollupDocument>.Empty).ToListAsync();
            var match = docs.FirstOrDefault(doc => doc.Endpoint == "match-v5.match");
            return docs.Count == 3 && match is { Count: 2 };
        });

        await host.StopAsync();

        var documents = await collection.Find(FilterDefinition<RiotApiCallRollupDocument>.Empty).ToListAsync();
        documents.Should().HaveCount(3);
        documents.Should().OnlyContain(doc => doc.ProcessName == "Test");

        var match = documents.Single(doc => doc.Endpoint == "match-v5.match");
        match.StatusCode.Should().Be(200);
        match.Count.Should().Be(2);
        match.SumLatencyMs.Should().Be(200);
        // Last-seen rate-limit headers win within the bucket.
        match.AppRateLimitCount.Should().Be("3:1,57:120");

        var throttled = documents.Single(doc => doc.Endpoint == "match-v5.timeline");
        throttled.StatusCode.Should().Be(429);
        throttled.Count.Should().Be(1);
        throttled.RetryAfterSeconds.Should().Be(5);

        var faulted = documents.Single(doc => doc.Endpoint == "summoner-v4.byPuuid");
        faulted.StatusCode.Should().Be(0);
        faulted.Count.Should().Be(1);
    }

    [Fact]
    public async Task Sink_LaterCallWithoutHeaders_KeepsEarlierStoredValues()
    {
        await _mongo.ResetAsync();

        using var host = BuildHost();
        await host.StartAsync();

        var collection = _mongo.GetCollection<RiotApiCallRollupDocument>(MongoFixture.RiotApiCallsCollection);
        var at = DateTime.UtcNow;
        var recorder = host.Services.GetRequiredService<IRiotApiCallRecorder>();

        // A 429 carrying Retry-After + app rate-limit headers persists first.
        recorder.Record(Record("match-v5.timeline", 429, 30, at, appLimit: "20:1", appCount: "9:1", retryAfter: 5));
        await WaitUntilAsync(async () =>
        {
            var doc = await collection.Find(FilterDefinition<RiotApiCallRollupDocument>.Empty).FirstOrDefaultAsync();
            return doc is { Count: 1, RetryAfterSeconds: 5 };
        });

        // A later 429 in the same minute whose response had no Retry-After / headers
        // must fold into the same rollup (count 2) without a $set:null erasing the
        // values the first call stored.
        recorder.Record(Record("match-v5.timeline", 429, 40, at));
        await WaitUntilAsync(async () =>
        {
            var doc = await collection.Find(FilterDefinition<RiotApiCallRollupDocument>.Empty).FirstOrDefaultAsync();
            return doc is { Count: 2 };
        });

        await host.StopAsync();

        var rollup = await collection.Find(FilterDefinition<RiotApiCallRollupDocument>.Empty).SingleAsync();
        rollup.Count.Should().Be(2);
        rollup.SumLatencyMs.Should().Be(70);
        rollup.RetryAfterSeconds.Should().Be(5);
        rollup.AppRateLimitCount.Should().Be("9:1");
    }

    private IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMongoLogging(builder.Configuration, processName: "Test");
        builder.Services.Configure<MongoLoggingOptions>(options =>
        {
            options.ConnectionString = _mongo.ConnectionString;
            options.Database = MongoFixture.DatabaseName;
            options.RiotApiCallsCollection = MongoFixture.RiotApiCallsCollection;
            options.Enabled = true;
            options.FlushInterval = TimeSpan.FromMilliseconds(100);
        });
        return builder.Build();
    }

    private static RiotApiCallRecord Record(
        string endpoint,
        int statusCode,
        long latencyMs,
        DateTime at,
        string? appLimit = null,
        string? appCount = null,
        int? retryAfter = null)
        => new(
            at,
            endpoint,
            "GET",
            statusCode,
            latencyMs,
            Route: "europe",
            AppRateLimit: appLimit,
            AppRateLimitCount: appCount,
            MethodRateLimit: null,
            MethodRateLimitCount: null,
            RetryAfterSeconds: retryAfter,
            RateLimitType: null);

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("Condition not met within the timeout.");
    }
}
