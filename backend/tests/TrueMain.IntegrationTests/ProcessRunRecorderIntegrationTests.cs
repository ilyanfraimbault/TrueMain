using Data.Entities;
using Ingestor.Services;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public sealed class ProcessRunRecorderIntegrationTests
{
    private readonly PostgresFixture _fixture;

    public ProcessRunRecorderIntegrationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RecordStartThenSuccess_StampsTheCurrentIteration_OnTheFinalisedRow()
    {
        await _fixture.ResetDatabaseAsync();

        var iterationContext = new IterationContext();
        var recorder = new ProcessRunRecorder(_fixture.CreateSessionFactory(), iterationContext);

        Guid runId;
        Guid iterationId;
        using (var iteration = iterationContext.BeginIteration())
        {
            iterationId = iteration.IterationId;
            var startedAt = DateTime.UtcNow;
            runId = await recorder.RecordStartAsync("Discovery", startedAt, CancellationToken.None);

            // The in-flight Running row already carries the iteration.
            await using (var db = _fixture.CreateDbContext())
            {
                var running = await db.ProcessRuns.AsNoTracking().SingleAsync(run => run.Id == runId);
                running.IterationId.Should().Be(iterationId);
                running.Status.Should().Be(ProcessRunStatus.Running);
            }

            await recorder.RecordAsync(
                runId,
                "Discovery",
                startedAt,
                startedAt.AddSeconds(2),
                ProcessRunStatus.Success,
                summary: null,
                error: null,
                CancellationToken.None);
        }

        // Finalising in place keeps the iteration and flips the status.
        await using (var db = _fixture.CreateDbContext())
        {
            var finalised = await db.ProcessRuns.AsNoTracking().SingleAsync(run => run.Id == runId);
            finalised.IterationId.Should().Be(iterationId);
            finalised.Status.Should().Be(ProcessRunStatus.Success);
        }
    }

    [Fact]
    public async Task RecordStart_OutsideAnyIteration_LeavesIterationNull()
    {
        await _fixture.ResetDatabaseAsync();

        var recorder = new ProcessRunRecorder(_fixture.CreateSessionFactory(), new IterationContext());

        var startedAt = DateTime.UtcNow;
        var runId = await recorder.RecordStartAsync("AdHoc", startedAt, CancellationToken.None);

        await using var db = _fixture.CreateDbContext();
        var run = await db.ProcessRuns.AsNoTracking().SingleAsync(r => r.Id == runId);
        run.IterationId.Should().BeNull();
    }

    [Fact]
    public async Task ReconcileOrphanedRunsAsync_FlipsOnlyRunningRowsToAbandoned()
    {
        await _fixture.ResetDatabaseAsync();

        var startedAt = DateTime.UtcNow.AddMinutes(-10);
        Guid orphanId;
        Guid successId;

        await using (var db = _fixture.CreateDbContext())
        {
            // An orphaned in-flight row (its owning process died) and a settled
            // success that reconciliation must leave untouched.
            var orphan = new ProcessRun
            {
                ProcessName = "Discovery",
                StartedAtUtc = startedAt,
                FinishedAtUtc = startedAt,
                DurationMs = 0,
                Status = ProcessRunStatus.Running,
                Host = "dead-host",
                LastHeartbeatAtUtc = startedAt
            };
            var success = new ProcessRun
            {
                ProcessName = "Scoring",
                StartedAtUtc = startedAt,
                FinishedAtUtc = startedAt.AddMinutes(1),
                DurationMs = 60_000,
                Status = ProcessRunStatus.Success,
                Host = "dead-host"
            };
            db.ProcessRuns.AddRange(orphan, success);
            await db.SaveChangesAsync();
            orphanId = orphan.Id;
            successId = success.Id;
        }

        var recorder = new ProcessRunRecorder(_fixture.CreateSessionFactory(), new IterationContext());
        var reconciled = await recorder.ReconcileOrphanedRunsAsync(CancellationToken.None);

        reconciled.Should().Be(1);

        await using var verify = _fixture.CreateDbContext();

        var orphanAfter = await verify.ProcessRuns.AsNoTracking().SingleAsync(run => run.Id == orphanId);
        orphanAfter.Status.Should().Be(ProcessRunStatus.Abandoned);
        orphanAfter.Error.Should().Contain("Abandoned");
        // A real finish time + non-zero duration so it stops reading as a
        // zero-duration in-flight row.
        orphanAfter.FinishedAtUtc.Should().BeAfter(startedAt);
        orphanAfter.DurationMs.Should().BeGreaterThan(0);

        var successAfter = await verify.ProcessRuns.AsNoTracking().SingleAsync(run => run.Id == successId);
        successAfter.Status.Should().Be(ProcessRunStatus.Success);
    }

    [Fact]
    public async Task ReconcileOrphanedRunsAsync_ReturnsZero_WhenNothingIsRunning()
    {
        await _fixture.ResetDatabaseAsync();

        var startedAt = DateTime.UtcNow.AddMinutes(-5);
        await using (var db = _fixture.CreateDbContext())
        {
            db.ProcessRuns.Add(new ProcessRun
            {
                ProcessName = "Discovery",
                StartedAtUtc = startedAt,
                FinishedAtUtc = startedAt.AddMinutes(1),
                DurationMs = 60_000,
                Status = ProcessRunStatus.Success,
                Host = "host"
            });
            await db.SaveChangesAsync();
        }

        var recorder = new ProcessRunRecorder(_fixture.CreateSessionFactory(), new IterationContext());
        (await recorder.ReconcileOrphanedRunsAsync(CancellationToken.None)).Should().Be(0);
    }
}
