using AwesomeAssertions;
using Data.Logging.Mongo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Exercises the two MongoDB-backed write paths against a real Mongo container:
/// the batched diagnostic <see cref="MongoLogSink"/> (drains the channel,
/// inserts, creates the TTL index) and the lossless <see cref="MongoAuditLog"/>
/// (synchronous insert into <c>audit_events</c>).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class MongoLoggingIntegrationTests
{
    private readonly MongoFixture _mongo;

    public MongoLoggingIntegrationTests(MongoFixture mongo)
    {
        _mongo = mongo;
    }

    [Fact]
    public async Task DiagnosticSink_DrainsChannelAndPersistsWarningsToLogsCollection()
    {
        await _mongo.ResetAsync();

        using var host = BuildHost();
        await host.StartAsync();

        var collection = _mongo.GetCollection<MongoLogDocument>(MongoFixture.LogsCollection);

        // The sink creates its indexes once on startup; wait for the TTL retention
        // index before logging so the assertion below is not racing startup.
        await WaitUntilAsync(async () =>
        {
            var names = (await collection.Indexes.List().ToListAsync())
                .Select(index => index["name"].AsString);
            return names.Contains("ttl_timestamp");
        });

        // A Warning at the configured minimum must be persisted; an Information
        // below it must be dropped.
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Test.Category");
        logger.LogInformation("below threshold, dropped");
        logger.LogWarning("persisted warning");
        logger.LogError(new InvalidOperationException("boom"), "persisted error");

        // The sink flushes on its 100ms window; poll until the two qualifying
        // records land rather than depending on shutdown timing.
        await WaitUntilAsync(async () =>
            await collection.CountDocumentsAsync(FilterDefinition<MongoLogDocument>.Empty) == 2);

        await host.StopAsync();

        var documents = await collection.Find(FilterDefinition<MongoLogDocument>.Empty).ToListAsync();

        documents.Should().HaveCount(2);
        documents.Should().OnlyContain(doc => doc.Level == "Warning" || doc.Level == "Error");
        documents.Should().Contain(doc => doc.Message == "persisted warning");
        var errorDoc = documents.Single(doc => doc.Level == "Error");
        errorDoc.Exception.Should().Contain("InvalidOperationException");

        // The TTL retention index enforces the diagnostic-log retention window.
        var indexes = await collection.Indexes.List().ToListAsync();
        indexes.Should().Contain(index => index["name"] == "ttl_timestamp");
    }

    /// <summary>
    /// Polls <paramref name="condition"/> until it is true or the timeout elapses,
    /// so a test can wait on the asynchronous sink without a fixed sleep.
    /// </summary>
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

    [Fact]
    public async Task AuditLog_WritesLosslesslyToAuditEventsCollection()
    {
        await _mongo.ResetAsync();

        using var host = BuildHost();
        // No need to start the host for the audit writer; it inserts synchronously.
        var auditLog = host.Services.GetRequiredService<IAuditLog>();
        auditLog.IsEnabled.Should().BeTrue();

        await auditLog.RecordAsync(
            action: "seed_account",
            actor: "operator",
            targetType: "SeedRequest",
            targetId: "abc-123",
            metadata: new Dictionary<string, string>
            {
                ["gameName"] = "Phantasm",
                ["platformId"] = "EUW1"
            });

        var collection = _mongo.GetCollection<AuditEventDocument>(MongoFixture.AuditCollection);
        var events = await collection.Find(FilterDefinition<AuditEventDocument>.Empty).ToListAsync();

        events.Should().ContainSingle();
        var recorded = events[0];
        recorded.Action.Should().Be("seed_account");
        recorded.Actor.Should().Be("operator");
        recorded.TargetType.Should().Be("SeedRequest");
        recorded.TargetId.Should().Be("abc-123");
        recorded.Metadata.Should().NotBeNull();
        recorded.Metadata!["gameName"].Should().Be("Phantasm");
        recorded.TimestampUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    private IHost BuildHost() =>
        Host.CreateApplicationBuilder()
            .ConfigureMongoLogging(_mongo)
            .Build();
}

file static class MongoLoggingTestHostExtensions
{
    public static HostApplicationBuilder ConfigureMongoLogging(
        this HostApplicationBuilder builder,
        MongoFixture mongo)
    {
        builder.Services.AddMongoLogging(builder.Configuration, processName: "Test");
        builder.Services.Configure<MongoLoggingOptions>(options =>
        {
            options.ConnectionString = mongo.ConnectionString;
            options.Database = MongoFixture.DatabaseName;
            options.LogsCollection = MongoFixture.LogsCollection;
            options.AuditCollection = MongoFixture.AuditCollection;
            options.Enabled = true;
            options.MinimumLevel = LogLevel.Warning;
            options.FlushInterval = TimeSpan.FromMilliseconds(100);
        });
        return builder;
    }
}
