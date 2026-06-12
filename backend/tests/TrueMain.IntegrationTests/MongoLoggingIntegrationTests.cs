using AwesomeAssertions;
using Data.Logging;
using Data.Logging.Mongo;
using Microsoft.Extensions.Configuration;
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

    [Fact]
    public async Task DiagnosticSink_PersistsRegisteredOpsEventsBelowMinimumLevel()
    {
        await _mongo.ResetAsync();

        using var host = BuildHost();
        await host.StartAsync();

        var collection = _mongo.GetCollection<MongoLogDocument>(MongoFixture.LogsCollection);
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Test.OpsEvents");

        // The floor stays Warning, yet registered ops events (#444) must be
        // persisted from Information up — and ONLY them: a plain Information line,
        // an unregistered EventId and a registered name with the wrong id all stay
        // below the floor and are dropped.
        logger.LogInformation("plain information, dropped");
        logger.LogInformation(new EventId(999, "NotAnOpsEvent"), "unregistered event, dropped");
        logger.LogInformation(
            new EventId(1, nameof(OpsEvents.CandidateValidated)),
            "right name, wrong id, dropped");
        logger.LogInformation(
            OpsEvents.CandidateValidated,
            "Validated {Count} candidates for {Platform}/{Puuid}.",
            2,
            "EUW1",
            "puuid-1");
        // An ops event logged at/above the floor keeps its eventType stamp too.
        logger.LogWarning(OpsEvents.SeedRequestFailed, "Seed request failed.");
        logger.LogWarning("plain warning, persisted without eventType");

        await WaitUntilAsync(async () =>
            await collection.CountDocumentsAsync(FilterDefinition<MongoLogDocument>.Empty) == 3);

        await host.StopAsync();

        var documents = await collection.Find(FilterDefinition<MongoLogDocument>.Empty).ToListAsync();
        documents.Should().HaveCount(3);

        var candidateValidated = documents.Single(doc => doc.EventType == nameof(OpsEvents.CandidateValidated));
        candidateValidated.Level.Should().Be("Information");
        candidateValidated.Message.Should().Be("Validated 2 candidates for EUW1/puuid-1.");
        candidateValidated.EventId.Should().Be(OpsEvents.CandidateValidated.Id);

        var seedRequestFailed = documents.Single(doc => doc.EventType == nameof(OpsEvents.SeedRequestFailed));
        seedRequestFailed.Level.Should().Be("Warning");

        var plainWarning = documents.Single(doc => doc.EventType is null);
        plainWarning.Message.Should().Be("plain warning, persisted without eventType");
    }

    [Fact]
    public async Task DiagnosticSink_AppliesPerProviderCategoryRulesFromConfiguration()
    {
        await _mongo.ResetAsync();

        // The production appsettings silence Polly resilience telemetry below
        // Error for the Mongo provider only ("Logging:Mongo:LogLevel:Polly").
        // The [ProviderAlias("Mongo")] rule is applied by the logging factory
        // BEFORE MongoLogger is called, so the retry chatter never reaches the
        // channel. Assert that exact mechanism with the production rule value.
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["Logging:Mongo:LogLevel:Polly"] = "Error"
        });
        await host.StartAsync();

        var collection = _mongo.GetCollection<MongoLogDocument>(MongoFixture.LogsCollection);
        var pollyLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Polly");
        var otherLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Test.Category");

        // The Warning pair every Riot 429 produces today: must NOT be persisted.
        pollyLogger.LogWarning("Execution attempt. Source: 'IRiotPlatformClient-standard//Standard-Retry'");
        pollyLogger.LogWarning("Resilience event occurred. EventName: 'OnRetry'");
        // Error-severity resilience telemetry (e.g. the circuit opening) survives.
        pollyLogger.LogError("Resilience event occurred. EventName: 'OnCircuitOpened'");
        // Other categories are untouched by the Polly rule.
        otherLogger.LogWarning("persisted warning");

        await WaitUntilAsync(async () =>
            await collection.CountDocumentsAsync(FilterDefinition<MongoLogDocument>.Empty) == 2);

        await host.StopAsync();

        var documents = await collection.Find(FilterDefinition<MongoLogDocument>.Empty).ToListAsync();
        documents.Should().HaveCount(2);
        documents.Should().NotContain(doc => doc.Category == "Polly" && doc.Level == "Warning");
        documents.Single(doc => doc.Category == "Polly").Level.Should().Be("Error");
        documents.Single(doc => doc.Category == "Test.Category").Level.Should().Be("Warning");
    }

    [Fact]
    public async Task EnsureIndexes_RecreatesTtlIndex_WhenRetentionChanges()
    {
        await _mongo.ResetAsync();

        var collection = _mongo.GetCollection<MongoLogDocument>(MongoFixture.LogsCollection);

        // First boot: a 30-day TTL window.
        await EnsureIndexesAsync(TimeSpan.FromDays(30));
        (await GetTtlExpireSecondsAsync(collection))
            .Should().Be((long)TimeSpan.FromDays(30).TotalSeconds);

        // Re-boot with a changed retention: the old DatabaseLogSink path would have
        // thrown IndexOptionsConflict and silently kept the stale 30-day window.
        // The reconciler must drop and recreate the index so the new window applies.
        await EnsureIndexesAsync(TimeSpan.FromDays(7));
        (await GetTtlExpireSecondsAsync(collection))
            .Should().Be((long)TimeSpan.FromDays(7).TotalSeconds);

        // Disabling retention tears the TTL index down entirely.
        await EnsureIndexesAsync(TimeSpan.Zero);
        (await GetTtlExpireSecondsAsync(collection)).Should().BeNull();
    }

    private async Task EnsureIndexesAsync(TimeSpan retention)
    {
        // The context owns an IMongoClient, so dispose it after each boot just as
        // the DI container would on host shutdown.
        using var context = new MongoLogContext(Microsoft.Extensions.Options.Options.Create(
            new MongoLoggingOptions
            {
                ConnectionString = _mongo.ConnectionString,
                Database = MongoFixture.DatabaseName,
                LogsCollection = MongoFixture.LogsCollection,
                AuditCollection = MongoFixture.AuditCollection,
                Enabled = true,
                LogsRetention = retention
            }));
        await context.EnsureIndexesAsync(CancellationToken.None);
    }

    private static async Task<long?> GetTtlExpireSecondsAsync(IMongoCollection<MongoLogDocument> collection)
    {
        var indexes = await collection.Indexes.List().ToListAsync();
        var ttl = indexes.FirstOrDefault(index => index["name"].AsString == "ttl_timestamp");
        return ttl is not null && ttl.TryGetValue("expireAfterSeconds", out var expire)
            ? expire.ToInt64()
            : null;
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

    private IHost BuildHost(IEnumerable<KeyValuePair<string, string?>>? extraConfiguration = null)
    {
        var builder = Host.CreateApplicationBuilder();

        // Per-provider filter rules (Logging:Mongo:LogLevel) are read from the
        // host configuration by the default logging setup, so injecting them here
        // exercises the exact path the production appsettings use.
        if (extraConfiguration is not null)
        {
            builder.Configuration.AddInMemoryCollection(extraConfiguration);
        }

        return builder
            .ConfigureMongoLogging(_mongo)
            .Build();
    }
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
