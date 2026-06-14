using AwesomeAssertions;
using Data.Entities;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ProcessRunCadenceQueryIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public ProcessRunCadenceQueryIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetLastCompletedRunStartAsync_ReturnsLatestCompleted_IgnoringRunning()
    {
        await _fixture.ResetDatabaseAsync();
        var now = new DateTime(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);
        var latestCompleted = now.AddHours(-1);

        await using (var db = _fixture.CreateDbContext())
        {
            db.ProcessRuns.AddRange(
                Run("Discovery", now.AddHours(-2), ProcessRunStatus.Success),
                Run("Discovery", latestCompleted, ProcessRunStatus.Success),
                // More recent, but still Running — must be ignored so the cadence gate reads
                // the prior cadence, not the iteration the recorder just opened.
                Run("Discovery", now.AddMinutes(-1), ProcessRunStatus.Running),
                // A different process at the same time must not leak into the Discovery query.
                Run("Scoring", now, ProcessRunStatus.Success));
            await db.SaveChangesAsync();
        }

        await using var session = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        var lastRun = await session.ProcessRuns.GetLastCompletedRunStartAsync("Discovery", CancellationToken.None);

        lastRun.Should().Be(latestCompleted);
    }

    [Fact]
    public async Task GetLastCompletedRunStartAsync_ReturnsNull_WhenNoCompletedRun()
    {
        await _fixture.ResetDatabaseAsync();

        await using (var db = _fixture.CreateDbContext())
        {
            db.ProcessRuns.Add(Run("Discovery", DateTime.UtcNow, ProcessRunStatus.Running));
            await db.SaveChangesAsync();
        }

        await using var session = await _fixture.CreateSessionFactory().CreateAsync(CancellationToken.None);
        var lastRun = await session.ProcessRuns.GetLastCompletedRunStartAsync("Discovery", CancellationToken.None);

        lastRun.Should().BeNull();
    }

    private static ProcessRun Run(string processName, DateTime startedAtUtc, ProcessRunStatus status) => new()
    {
        Id = Guid.NewGuid(),
        ProcessName = processName,
        StartedAtUtc = startedAtUtc,
        FinishedAtUtc = status == ProcessRunStatus.Running ? default : startedAtUtc.AddSeconds(5),
        DurationMs = status == ProcessRunStatus.Running ? 0 : 5000,
        Status = status
    };
}
