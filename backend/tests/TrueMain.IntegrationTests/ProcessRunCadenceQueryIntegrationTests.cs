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
    public async Task GetLastCompletedRunStartAsync_IgnoresSkipped_SoTheGuardCannotReArmItself()
    {
        await _mongo.ResetAsync();
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);
        var lastRealRun = now.AddHours(-20);

        await Collection().InsertManyAsync(
        [
            Run("Discovery", lastRealRun, ProcessRunStatus.Success),
            // The regression (#1149): the guard skipped every hourly iteration after the
            // real run, and each skip used to be recorded as Success. Reading those back
            // as "the last completed run" pushed the deadline forward on every iteration,
            // so the interval never elapsed and discovery ran exactly once, ever.
            Run("Discovery", now.AddHours(-3), ProcessRunStatus.Skipped),
            Run("Discovery", now.AddHours(-2), ProcessRunStatus.Skipped),
            Run("Discovery", now.AddHours(-1), ProcessRunStatus.Skipped)
        ]);

        using var context = BuildContext();
        var store = new ProcessRunStore(context);
        var lastRun = await store.GetLastCompletedRunStartAsync("Discovery", CancellationToken.None);

        // Measured against the real run, so a 1-day interval has 20h elapsed and the next
        // iteration past the 24h mark runs for real instead of skipping forever.
        lastRun.Should().Be(lastRealRun);
    }

    [Fact]
    public async Task GetLastCompletedRunStartAsync_ReturnsNull_WhenEveryRunWasSkipped()
    {
        await _mongo.ResetAsync();
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

        await Collection().InsertManyAsync(
        [
            Run("Discovery", now.AddHours(-2), ProcessRunStatus.Skipped),
            Run("Discovery", now.AddHours(-1), ProcessRunStatus.Skipped)
        ]);

        using var context = BuildContext();
        var store = new ProcessRunStore(context);
        var lastRun = await store.GetLastCompletedRunStartAsync("Discovery", CancellationToken.None);

        // Never actually ran, so the guard has nothing to measure against and must let the
        // process run rather than treating its own skips as a prior cadence.
        lastRun.Should().BeNull();
    }

    [Fact]
    public async Task GetLastCompletedRunStartAsync_CountsFailed_SoAFailedAttemptStillSpendsItsInterval()
    {
        await _mongo.ResetAsync();
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);
        var failedAttempt = now.AddHours(-1);

        await Collection().InsertManyAsync(
        [
            Run("Discovery", now.AddHours(-5), ProcessRunStatus.Success),
            Run("Discovery", failedAttempt, ProcessRunStatus.Failed)
        ]);

        using var context = BuildContext();
        var store = new ProcessRunStore(context);
        var lastRun = await store.GetLastCompletedRunStartAsync("Discovery", CancellationToken.None);

        // A failed attempt still called Riot, so it counts: the guard is a budget gate, not
        // a success gate.
        lastRun.Should().Be(failedAttempt);
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
