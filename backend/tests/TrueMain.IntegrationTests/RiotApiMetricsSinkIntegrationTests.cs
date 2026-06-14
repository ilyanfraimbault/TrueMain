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
/// startup and batch-inserts the records. Mirrors the diagnostic-sink integration
/// test (the codebase tests sinks against Mongo rather than with mocked
/// collections) (#93).
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
    public async Task Sink_DrainsRecordedCalls_PersistsThemAndCreatesIndexes()
    {
        await _mongo.ResetAsync();

        using var host = BuildHost();
        await host.StartAsync();

        var collection = _mongo.GetCollection<RiotApiCallDocument>(MongoFixture.RiotApiCallsCollection);

        // The sink creates its indexes once on startup; wait for the supporting
        // indexes so the assertion below is not racing startup.
        await WaitUntilAsync(async () =>
        {
            var names = (await collection.Indexes.List().ToListAsync())
                .Select(index => index["name"].AsString)
                .ToList();
            return names.Contains("ix_endpoint_timestamp") && names.Contains("ttl_timestamp");
        });

        var recorder = host.Services.GetRequiredService<IRiotApiCallRecorder>();
        recorder.Record(Record("match-v5.match", 200, 120, appLimit: "20:1,100:120", appCount: "3:1,57:120"));
        recorder.Record(Record("match-v5.timeline", 429, 30, retryAfter: 5));
        recorder.Record(Record("summoner-v4.byPuuid", 0, 1000));

        // The sink flushes on its short window; poll until the three records land.
        await WaitUntilAsync(async () =>
            await collection.CountDocumentsAsync(FilterDefinition<RiotApiCallDocument>.Empty) == 3);

        await host.StopAsync();

        var documents = await collection.Find(FilterDefinition<RiotApiCallDocument>.Empty).ToListAsync();
        documents.Should().HaveCount(3);
        documents.Should().OnlyContain(doc => doc.ProcessName == "Test");

        var match = documents.Single(doc => doc.Endpoint == "match-v5.match");
        match.StatusCode.Should().Be(200);
        match.LatencyMs.Should().Be(120);
        match.AppRateLimitCount.Should().Be("3:1,57:120");

        var throttled = documents.Single(doc => doc.Endpoint == "match-v5.timeline");
        throttled.StatusCode.Should().Be(429);
        throttled.RetryAfterSeconds.Should().Be(5);

        var faulted = documents.Single(doc => doc.Endpoint == "summoner-v4.byPuuid");
        faulted.StatusCode.Should().Be(0);
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
        string? appLimit = null,
        string? appCount = null,
        int? retryAfter = null)
        => new(
            DateTime.UtcNow,
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
