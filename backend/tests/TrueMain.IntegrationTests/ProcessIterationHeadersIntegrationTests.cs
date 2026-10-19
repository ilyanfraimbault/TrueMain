using AwesomeAssertions;
using Data.Entities;
using Data.Logging.Mongo;
using Data.Ops.Mongo;
using Ingestor.Options;
using Ingestor.Services;
using MongoDB.Driver;

namespace TrueMain.IntegrationTests;

/// <summary>
/// The <c>process_iterations</c> headers (#2124): written next to every iteration-stamped run
/// the ingestor records, built once from the runs written before them, and paged by
/// <see cref="ProcessRunStore.QueryIterationsAsync"/> without grouping the runs.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ProcessIterationHeadersIntegrationTests : IAsyncLifetime
{
    private readonly MongoFixture _mongo;

    public ProcessIterationHeadersIntegrationTests(MongoFixture mongo)
    {
        _mongo = mongo;
    }

    public async ValueTask InitializeAsync() => await _mongo.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task RecordingARun_UpsertsOneHeaderPerIteration_AtItsEarliestStart()
    {
        var iterationContext = new IterationContext();
        using var context = BuildContext();
        var recorder = new ProcessRunRecorder(new ProcessRunStore(context), iterationContext);
        var start = DateTime.UtcNow.AddMinutes(-10);

        Guid iterationId;
        using (var iteration = iterationContext.BeginIteration(JobMode.FetchLane))
        {
            iterationId = iteration.IterationId;
            await recorder.RecordStartAsync("Harvest", start.AddMinutes(2), CancellationToken.None);
            // A run starting earlier than the first one recorded pulls the header back.
            await recorder.RecordStartAsync("Discovery", start, CancellationToken.None);
        }

        // Outside a pass: no iteration, so no header.
        await recorder.RecordStartAsync("Scoring", start, CancellationToken.None);

        var headers = await Headers().Find(FilterDefinition<ProcessIterationDocument>.Empty).ToListAsync();
        headers.Should().ContainSingle();
        headers[0].Id.Should().Be(iterationId);
        headers[0].JobMode.Should().Be("FetchLane");
        headers[0].StartedAtUtc.Should().BeCloseTo(start, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task QueryIterations_BackfillsTheHeadersOfRunsWrittenBeforeThem()
    {
        var now = DateTime.UtcNow;
        var old = Guid.NewGuid();
        // Written before the headers existed: straight into process_runs, a month back.
        await Runs().InsertManyAsync(
        [
            BuildRun(old, "MainAnalysis", now.AddDays(-30).AddMinutes(5), "AggregateLane"),
            BuildRun(old, "MatchTeamPositionCorrection", now.AddDays(-30), "AggregateLane")
        ]);

        using var context = BuildContext();
        var store = new ProcessRunStore(context);
        // Written after the deploy: through the store, so it already has its header.
        var recent = Guid.NewGuid();
        await store.InsertAsync(BuildRun(recent, "Harvest", now.AddMinutes(-1), "FetchLane"), CancellationToken.None);

        var page = await store.QueryIterationsAsync(1, 10, false, null, now.AddMinutes(-5), CancellationToken.None);

        page.Total.Should().Be(2);
        page.Headers.Select(header => header.IterationId).Should().Equal(recent, old);
        page.Headers[1].StartedAtUtc.Should().BeCloseTo(now.AddDays(-30), TimeSpan.FromMilliseconds(1));
        var backfilled = await Headers().Find(doc => doc.Id == old).SingleAsync();
        backfilled.JobMode.Should().Be("AggregateLane");
    }

    [Fact]
    public async Task QueryIterations_DoesNotBackfill_RunsJustOlderThanTheOldestHeader()
    {
        // The leftover runs of a pass whose header the TTL reaped first sit a few hours
        // before the oldest header; rebuilding a header for them on every host start would
        // re-scan the runs each time, so only history days older than the headers counts.
        var now = DateTime.UtcNow;
        await Runs().InsertOneAsync(BuildRun(Guid.NewGuid(), "Discovery", now.AddHours(-3), "FetchLane"));

        using var context = BuildContext();
        var store = new ProcessRunStore(context);
        var kept = Guid.NewGuid();
        await store.InsertAsync(BuildRun(kept, "Discovery", now.AddMinutes(-1), "FetchLane"), CancellationToken.None);

        var page = await store.QueryIterationsAsync(1, 10, false, null, now.AddMinutes(-5), CancellationToken.None);

        page.Total.Should().Be(1);
        page.Headers.Select(header => header.IterationId).Should().Equal(kept);
    }

    private IMongoCollection<ProcessRunDocument> Runs()
        => _mongo.GetCollection<ProcessRunDocument>(MongoFixture.ProcessRunsCollection);

    private IMongoCollection<ProcessIterationDocument> Headers()
        => _mongo.GetCollection<ProcessIterationDocument>(MongoFixture.ProcessIterationsCollection);

    private static ProcessRunDocument BuildRun(Guid iterationId, string processName, DateTime startedAtUtc, string jobMode)
        => new()
        {
            Id = Guid.NewGuid(),
            IterationId = iterationId,
            ProcessName = processName,
            JobMode = jobMode,
            StartedAtUtc = startedAtUtc,
            FinishedAtUtc = startedAtUtc.AddMinutes(1),
            DurationMs = 60_000,
            Status = ProcessRunStatus.Success,
            Host = "test-host"
        };

    private MongoLogContext BuildContext()
        => new(Microsoft.Extensions.Options.Options.Create(new MongoLoggingOptions
        {
            ConnectionString = _mongo.ConnectionString,
            Database = MongoFixture.DatabaseName,
            ProcessRunsCollection = MongoFixture.ProcessRunsCollection,
            ProcessIterationsCollection = MongoFixture.ProcessIterationsCollection,
            Enabled = true
        }));
}
