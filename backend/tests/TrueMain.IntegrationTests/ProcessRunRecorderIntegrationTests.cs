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
}
