using AwesomeAssertions;
using Data.Entities;
using Data.Logging.Mongo;
using Data.Ops.Mongo;
using Ingestor.Processes.Summaries;
using Ingestor.Services;
using MongoDB.Driver;

namespace TrueMain.IntegrationTests;

/// <summary>
/// Exercises <see cref="ProcessRunRecorder"/> against the Mongo-backed
/// <see cref="ProcessRunStore"/> (process runs moved off Postgres with the rest
/// of the admin-portal data).
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ProcessRunRecorderIntegrationTests
{
    private readonly MongoFixture _mongo;

    public ProcessRunRecorderIntegrationTests(MongoFixture mongo)
    {
        _mongo = mongo;
    }

    [Fact]
    public async Task RecordStartThenSuccess_StampsTheCurrentIteration_OnTheFinalisedRun()
    {
        await _mongo.ResetAsync();

        var iterationContext = new IterationContext();
        using var context = BuildContext();
        var recorder = new ProcessRunRecorder(new ProcessRunStore(context), iterationContext);

        Guid runId;
        Guid iterationId;
        using (var iteration = iterationContext.BeginIteration())
        {
            iterationId = iteration.IterationId;
            var startedAt = DateTime.UtcNow;
            runId = await recorder.RecordStartAsync("Discovery", startedAt, CancellationToken.None);

            // The in-flight Running document already carries the iteration.
            var running = await GetRunAsync(runId);
            running.IterationId.Should().Be(iterationId);
            running.Status.Should().Be(ProcessRunStatus.Running);

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
        var finalised = await GetRunAsync(runId);
        finalised.IterationId.Should().Be(iterationId);
        finalised.Status.Should().Be(ProcessRunStatus.Success);
    }

    [Fact]
    public async Task RecordAsync_PersistsTheSummaryWithItsCamelCaseKeys()
    {
        await _mongo.ResetAsync();

        using var context = BuildContext();
        var recorder = new ProcessRunRecorder(new ProcessRunStore(context), new IterationContext());
        var startedAt = DateTime.UtcNow;
        var runId = await recorder.RecordStartAsync("Discovery", startedAt, CancellationToken.None);

        await recorder.RecordAsync(
            runId,
            "Discovery",
            startedAt,
            startedAt.AddSeconds(2),
            ProcessRunStatus.Success,
            new DiscoverySummary([new DiscoveryPlatformSummary("EUW1", 40, 3, 12, 5, 2, 6, 32, null)]),
            error: null,
            CancellationToken.None);

        // The stored JSON is what the admin portal renders, so assert on the
        // persisted keys rather than on the in-memory record (#268): the summaries
        // were anonymous types with camelCase members and that shape is persisted.
        var run = await GetRunAsync(runId);
        run.SummaryJson.Should().NotBeNull();

        using var summary = System.Text.Json.JsonDocument.Parse(run.SummaryJson!);
        var platform = summary.RootElement
            .GetProperty("platforms")
            .EnumerateArray()
            .Single();

        platform.GetProperty("platform").GetString().Should().Be("EUW1");
        platform.GetProperty("accountsProcessed").GetInt32().Should().Be(40);
        platform.GetProperty("newAccounts").GetInt32().Should().Be(3);
        platform.GetProperty("candidatesInserted").GetInt32().Should().Be(12);
        platform.GetProperty("candidatesUpdated").GetInt32().Should().Be(5);
        platform.GetProperty("rankSnapshotsInserted").GetInt32().Should().Be(2);
        platform.GetProperty("rankSnapshotsUpdated").GetInt32().Should().Be(6);
        platform.GetProperty("rankSnapshotsUnchanged").GetInt32().Should().Be(32);
        platform.GetProperty("error").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task RecordStart_OutsideAnyIteration_LeavesIterationNull()
    {
        await _mongo.ResetAsync();

        using var context = BuildContext();
        var recorder = new ProcessRunRecorder(new ProcessRunStore(context), new IterationContext());

        var startedAt = DateTime.UtcNow;
        var runId = await recorder.RecordStartAsync("AdHoc", startedAt, CancellationToken.None);

        var run = await GetRunAsync(runId);
        run.IterationId.Should().BeNull();
    }

    [Fact]
    public async Task ReconcileOrphanedRunsAsync_FlipsOnlyRunningRunsToAbandoned()
    {
        await _mongo.ResetAsync();

        var startedAt = DateTime.UtcNow.AddMinutes(-10);

        // An orphaned in-flight run (its owning process died) and a settled
        // success that reconciliation must leave untouched.
        var orphanId = Guid.NewGuid();
        var successId = Guid.NewGuid();
        await Collection().InsertManyAsync(
        [
            new ProcessRunDocument
            {
                Id = orphanId,
                ProcessName = "Discovery",
                StartedAtUtc = startedAt,
                FinishedAtUtc = startedAt,
                DurationMs = 0,
                Status = ProcessRunStatus.Running,
                Host = "dead-host",
                LastHeartbeatAtUtc = startedAt
            },
            new ProcessRunDocument
            {
                Id = successId,
                ProcessName = "Scoring",
                StartedAtUtc = startedAt,
                FinishedAtUtc = startedAt.AddMinutes(1),
                DurationMs = 60_000,
                Status = ProcessRunStatus.Success,
                Host = "dead-host"
            }
        ]);

        using var context = BuildContext();
        var recorder = new ProcessRunRecorder(new ProcessRunStore(context), new IterationContext());
        var reconciled = await recorder.ReconcileOrphanedRunsAsync(["Discovery", "Scoring"], CancellationToken.None);

        reconciled.Should().Be(1);

        var orphanAfter = await GetRunAsync(orphanId);
        orphanAfter.Status.Should().Be(ProcessRunStatus.Abandoned);
        orphanAfter.Error.Should().Contain("Abandoned");
        // A real finish time + non-zero duration so it stops reading as a
        // zero-duration in-flight run.
        orphanAfter.FinishedAtUtc.Should().BeAfter(startedAt);
        orphanAfter.DurationMs.Should().BeGreaterThan(0);

        var successAfter = await GetRunAsync(successId);
        successAfter.Status.Should().Be(ProcessRunStatus.Success);
    }

    [Fact]
    public async Task ReconcileOrphanedRunsAsync_LeavesAnotherLanesRunningRunAlone()
    {
        await _mongo.ResetAsync();

        var startedAt = DateTime.UtcNow.AddMinutes(-3);
        var ownId = Guid.NewGuid();
        var otherLaneId = Guid.NewGuid();
        await Collection().InsertManyAsync(
        [
            new ProcessRunDocument
            {
                Id = ownId,
                ProcessName = "Discovery",
                StartedAtUtc = startedAt,
                FinishedAtUtc = startedAt,
                DurationMs = 0,
                Status = ProcessRunStatus.Running,
                Host = "fetch-lane",
                LastHeartbeatAtUtc = startedAt
            },
            new ProcessRunDocument
            {
                Id = otherLaneId,
                ProcessName = "ChampionPatternAggregation",
                StartedAtUtc = startedAt,
                FinishedAtUtc = startedAt,
                DurationMs = 0,
                Status = ProcessRunStatus.Running,
                Host = "aggregate-lane",
                LastHeartbeatAtUtc = startedAt
            }
        ]);

        using var context = BuildContext();
        var recorder = new ProcessRunRecorder(new ProcessRunStore(context), new IterationContext());

        // The fetch lane restarting must reclaim its own orphan and leave the aggregate
        // lane's genuinely in-flight run alone (#1362) — the two lanes are separate
        // processes against one database.
        var reconciled = await recorder.ReconcileOrphanedRunsAsync(["Discovery", "MatchIngestion"], CancellationToken.None);

        reconciled.Should().Be(1);
        (await GetRunAsync(ownId)).Status.Should().Be(ProcessRunStatus.Abandoned);
        (await GetRunAsync(otherLaneId)).Status.Should().Be(ProcessRunStatus.Running);
    }

    [Fact]
    public async Task ReconcileOrphanedRunsAsync_ReturnsZero_WhenNothingIsRunning()
    {
        await _mongo.ResetAsync();

        var startedAt = DateTime.UtcNow.AddMinutes(-5);
        await Collection().InsertOneAsync(new ProcessRunDocument
        {
            Id = Guid.NewGuid(),
            ProcessName = "Discovery",
            StartedAtUtc = startedAt,
            FinishedAtUtc = startedAt.AddMinutes(1),
            DurationMs = 60_000,
            Status = ProcessRunStatus.Success,
            Host = "host"
        });

        using var context = BuildContext();
        var recorder = new ProcessRunRecorder(new ProcessRunStore(context), new IterationContext());
        (await recorder.ReconcileOrphanedRunsAsync(["Discovery"], CancellationToken.None)).Should().Be(0);
    }

    private IMongoCollection<ProcessRunDocument> Collection()
        => _mongo.GetCollection<ProcessRunDocument>(MongoFixture.ProcessRunsCollection);

    private async Task<ProcessRunDocument> GetRunAsync(Guid id)
        => await Collection().Find(doc => doc.Id == id).SingleAsync();

    private MongoLogContext BuildContext()
        => new(Microsoft.Extensions.Options.Options.Create(new MongoLoggingOptions
        {
            ConnectionString = _mongo.ConnectionString,
            Database = MongoFixture.DatabaseName,
            ProcessRunsCollection = MongoFixture.ProcessRunsCollection,
            Enabled = true
        }));
}
