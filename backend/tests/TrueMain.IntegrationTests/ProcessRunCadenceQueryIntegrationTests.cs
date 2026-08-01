using AwesomeAssertions;
using Data.Entities;
using Data.Logging.Mongo;
using Data.Ops.Mongo;
using MongoDB.Driver;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ProcessRunCadenceQueryIntegrationTests
{
    private readonly MongoFixture _mongo;

    public ProcessRunCadenceQueryIntegrationTests(MongoFixture mongo)
    {
        _mongo = mongo;
    }

    [Fact]
    public async Task GetLastCompletedRunStartAsync_ReturnsLatestCompleted_IgnoringRunning()
    {
        await _mongo.ResetAsync();
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);
        var latestCompleted = now.AddHours(-1);

        await Collection().InsertManyAsync(
        [
            Run("Discovery", now.AddHours(-2), ProcessRunStatus.Success),
            Run("Discovery", latestCompleted, ProcessRunStatus.Success),
            // More recent, but still Running — must be ignored so the cadence gate reads
            // the prior cadence, not the iteration the recorder just opened.
            Run("Discovery", now.AddMinutes(-1), ProcessRunStatus.Running),
            // A different process at the same time must not leak into the Discovery query.
            Run("Scoring", now, ProcessRunStatus.Success)
        ]);

        using var context = BuildContext();
        var store = new ProcessRunStore(context);
        var lastRun = await store.GetLastCompletedRunStartAsync("Discovery", CancellationToken.None);

        lastRun.Should().Be(latestCompleted);
    }

    [Fact]
    public async Task GetLastCompletedRunStartAsync_ReturnsNull_WhenNoCompletedRun()
    {
        await _mongo.ResetAsync();

        await Collection().InsertOneAsync(Run("Discovery", DateTime.UtcNow, ProcessRunStatus.Running));

        using var context = BuildContext();
        var store = new ProcessRunStore(context);
        var lastRun = await store.GetLastCompletedRunStartAsync("Discovery", CancellationToken.None);

        lastRun.Should().BeNull();
    }

    private IMongoCollection<ProcessRunDocument> Collection()
        => _mongo.GetCollection<ProcessRunDocument>(MongoFixture.ProcessRunsCollection);

    private MongoLogContext BuildContext()
        => new(Microsoft.Extensions.Options.Options.Create(new MongoLoggingOptions
        {
            ConnectionString = _mongo.ConnectionString,
            Database = MongoFixture.DatabaseName,
            ProcessRunsCollection = MongoFixture.ProcessRunsCollection,
            Enabled = true
        }));

    private static ProcessRunDocument Run(string processName, DateTime startedAtUtc, ProcessRunStatus status) => new()
    {
        Id = Guid.NewGuid(),
        ProcessName = processName,
        StartedAtUtc = startedAtUtc,
        FinishedAtUtc = status == ProcessRunStatus.Running ? startedAtUtc : startedAtUtc.AddSeconds(5),
        DurationMs = status == ProcessRunStatus.Running ? 0 : 5000,
        Status = status
    };
}
