using AwesomeAssertions;
using Data.Entities;
using Data.Logging.Mongo;
using Data.Ops.Mongo;
using MongoDB.Driver;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ProcessRunCadenceQueryIntegrationTests : IAsyncLifetime
{
    private readonly MongoFixture _mongo;

    public ProcessRunCadenceQueryIntegrationTests(MongoFixture mongo)
    {
        _mongo = mongo;
    }

    public async ValueTask InitializeAsync() => await _mongo.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetLastCompletedRunStartAsync_ReturnsLatestCompleted_IgnoringRunning()
    {
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
        var lastRun = await store.GetLastCompletedRunStartAsync("Discovery", now, CancellationToken.None);

        lastRun.Should().Be(latestCompleted);
    }

    [Fact]
    public async Task GetLastCompletedRunStartAsync_IgnoresSkipped_SoTheGuardCannotReArmItself()
    {
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
        var lastRun = await store.GetLastCompletedRunStartAsync("Discovery", now, CancellationToken.None);

        // Measured against the real run, so a 1-day interval has 20h elapsed and the next
        // iteration past the 24h mark runs for real instead of skipping forever.
        lastRun.Should().Be(lastRealRun);
    }

    [Fact]
    public async Task GetLastCompletedRunStartAsync_ReturnsNull_WhenEveryRunWasSkipped()
    {
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

        await Collection().InsertManyAsync(
        [
            Run("Discovery", now.AddHours(-2), ProcessRunStatus.Skipped),
            Run("Discovery", now.AddHours(-1), ProcessRunStatus.Skipped)
        ]);

        using var context = BuildContext();
        var store = new ProcessRunStore(context);
        var lastRun = await store.GetLastCompletedRunStartAsync("Discovery", now, CancellationToken.None);

        // Never actually ran, so the guard has nothing to measure against and must let the
        // process run rather than treating its own skips as a prior cadence.
        lastRun.Should().BeNull();
    }

    [Fact]
    public async Task GetLastCompletedRunStartAsync_CountsFailed_SoAFailedAttemptStillSpendsItsInterval()
    {
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);
        var failedAttempt = now.AddHours(-1);

        await Collection().InsertManyAsync(
        [
            Run("Discovery", now.AddHours(-5), ProcessRunStatus.Success),
            Run("Discovery", failedAttempt, ProcessRunStatus.Failed)
        ]);

        using var context = BuildContext();
        var store = new ProcessRunStore(context);
        var lastRun = await store.GetLastCompletedRunStartAsync("Discovery", now, CancellationToken.None);

        // A failed attempt still called Riot, so it counts: the guard is a budget gate, not
        // a success gate.
        lastRun.Should().Be(failedAttempt);
    }

    [Fact]
    public async Task GetLastCompletedRunStartAsync_ReturnsNull_WhenNoCompletedRun()
    {
        await Collection().InsertOneAsync(Run("Discovery", DateTime.UtcNow, ProcessRunStatus.Running));

        using var context = BuildContext();
        var store = new ProcessRunStore(context);
        var lastRun = await store.GetLastCompletedRunStartAsync("Discovery", DateTime.UtcNow, CancellationToken.None);

        lastRun.Should().BeNull();
    }

    [Fact]
    public async Task GetLastCompletedRunStartAsync_CountsAStaleRunningRow_AsTheAbandonedRunItIs()
    {
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);
        var deadRunStart = now.AddHours(-1);

        // A run whose host died an hour ago without anyone closing it: stored Running,
        // effectively Abandoned (#2474), so it started its work and counts.
        var dead = Run("Discovery", deadRunStart, ProcessRunStatus.Running);
        dead.LastHeartbeatAtUtc = deadRunStart.AddMinutes(5);
        await Collection().InsertManyAsync(
        [
            Run("Discovery", now.AddHours(-3), ProcessRunStatus.Success),
            dead,
            // The caller's own in-flight row, beating: still excluded.
            Run("Discovery", now.AddSeconds(-30), ProcessRunStatus.Running)
        ]);

        using var context = BuildContext();
        var store = new ProcessRunStore(context);
        var lastRun = await store.GetLastCompletedRunStartAsync("Discovery", now, CancellationToken.None);

        lastRun.Should().Be(deadRunStart);
    }

    [Fact]
    public async Task CountFailuresSinceAsync_CountsAStaleRunningRow_AsAFailure()
    {
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);
        var lastSuccessFinish = now.AddHours(-4);

        var dead = Run("Discovery", now.AddHours(-1), ProcessRunStatus.Running);
        dead.LastHeartbeatAtUtc = now.AddMinutes(-50);
        var neverBeat = Run("Discovery", now.AddHours(-2), ProcessRunStatus.Running);
        neverBeat.LastHeartbeatAtUtc = null;

        await Collection().InsertManyAsync(
        [
            Run("Discovery", now.AddHours(-5), ProcessRunStatus.Failed),
            Run("Discovery", lastSuccessFinish.AddSeconds(-5), ProcessRunStatus.Success),
            Run("Discovery", now.AddHours(-3), ProcessRunStatus.Failed),
            neverBeat,
            dead,
            // Not failures: a skip, a redeploy's cancel and a live run.
            Run("Discovery", now.AddMinutes(-40), ProcessRunStatus.Skipped),
            Run("Discovery", now.AddMinutes(-30), ProcessRunStatus.Cancelled),
            Run("Discovery", now.AddSeconds(-30), ProcessRunStatus.Running),
            Run("Scoring", now.AddHours(-1), ProcessRunStatus.Failed)
        ]);

        using var context = BuildContext();
        var store = new ProcessRunStore(context);

        // Before #2474 the two dead hosts' rows counted for nothing: the streak read 1 and
        // the per-process alert, which waits for a streak, never opened.
        (await store.CountFailuresSinceAsync("Discovery", lastSuccessFinish, now, CancellationToken.None))
            .Should().Be(3);
        (await store.CountFailuresSinceAsync("Discovery", null, now, CancellationToken.None))
            .Should().Be(4);
    }

    [Fact]
    public async Task GetRollupsAsync_CountsAbandonedAndStaleRunningRows_AsFailures()
    {
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);
        var dead = Run("Discovery", now.AddHours(-1), ProcessRunStatus.Running);
        dead.LastHeartbeatAtUtc = now.AddMinutes(-50);

        await Collection().InsertManyAsync(
        [
            Run("Discovery", now.AddHours(-4), ProcessRunStatus.Success),
            Run("Discovery", now.AddHours(-3), ProcessRunStatus.Failed),
            Run("Discovery", now.AddHours(-2), ProcessRunStatus.Abandoned),
            dead,
            Run("Discovery", now.AddSeconds(-30), ProcessRunStatus.Running)
        ]);

        using var context = BuildContext();
        var store = new ProcessRunStore(context);
        var rollup = (await store.GetRollupsAsync("Discovery", windowStart: null, now, CancellationToken.None))
            .Should().ContainSingle().Subject;

        rollup.RunCountInWindow.Should().Be(5);
        rollup.FailureCountInWindow.Should().Be(3);
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
        Status = status,
        // A live run beats; the tests that want a dead one say so.
        LastHeartbeatAtUtc = startedAtUtc
    };
}
