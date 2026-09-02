using AwesomeAssertions;
using Data.Entities;
using Data.Ops.Mongo;
using Ingestor.Options;
using Ingestor.Services;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// #1362 split the pipeline into two lanes, so a pass no longer covers every process.
/// The mode is recorded on each run because the alternative — inferring the lane from the
/// processes that happen to have run — cannot tell a pass that has reached only its first
/// step from a deliberate single-process run.
/// </summary>
public sealed class ProcessRunJobModeTests
{
    [Fact]
    public async Task RecordStartAsync_StampsTheModeOfTheOpenIteration()
    {
        var store = Substitute.For<IProcessRunStore>();
        var context = new IterationContext();
        var recorder = new ProcessRunRecorder(store, context);

        using var iteration = context.BeginIteration(JobMode.AggregateLane);
        await recorder.RecordStartAsync("ChampionPatternAggregation", DateTime.UtcNow, CancellationToken.None);

        var document = store.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IProcessRunStore.InsertAsync))
            .GetArguments()[0]
            .Should().BeOfType<ProcessRunDocument>().Subject;

        document.JobMode.Should().Be(nameof(JobMode.AggregateLane));
        document.IterationId.Should().Be(iteration.IterationId);
    }

    [Fact]
    public async Task RecordStartAsync_LeavesTheModeNull_OutsideAnyIteration()
    {
        var store = Substitute.For<IProcessRunStore>();
        var recorder = new ProcessRunRecorder(store, new IterationContext());

        await recorder.RecordStartAsync("MatchDataRetention", DateTime.UtcNow, CancellationToken.None);

        var document = store.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IProcessRunStore.InsertAsync))
            .GetArguments()[0]
            .Should().BeOfType<ProcessRunDocument>().Subject;

        // A run recorded outside a pass has no lane to claim, and an invented one would
        // read as fact in the admin panel.
        document.JobMode.Should().BeNull();
    }

    [Fact]
    public async Task RecordAsync_StampsTheMode_OnTheRecoveryInsertToo()
    {
        var store = Substitute.For<IProcessRunStore>();
        // The in-flight document was reaped by the TTL before the run finished, so
        // finalising misses and the recorder inserts a fresh terminal document instead.
        store.FinalizeAsync(
                Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<ProcessRunStatus>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var context = new IterationContext();
        var recorder = new ProcessRunRecorder(store, context);

        using var iteration = context.BeginIteration(JobMode.FetchLane);
        var startedAt = DateTime.UtcNow;
        await recorder.RecordAsync(
            Guid.NewGuid(),
            "MatchIngestion",
            startedAt,
            startedAt.AddSeconds(1),
            ProcessRunStatus.Success,
            summary: null,
            error: null,
            CancellationToken.None);

        var document = store.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IProcessRunStore.InsertAsync))
            .GetArguments()[0]
            .Should().BeOfType<ProcessRunDocument>().Subject;

        // Without this the recovered document is the one row of the pass with no lane,
        // and it is the only row a reader would have for a run that lost its original.
        document.JobMode.Should().Be(nameof(JobMode.FetchLane));
        document.IterationId.Should().Be(iteration.IterationId);
    }

    [Fact]
    public void BeginIteration_RestoresThePreviousModeOnDispose()
    {
        var context = new IterationContext();

        using (var outer = context.BeginIteration(JobMode.FetchLane))
        {
            context.CurrentJobMode.Should().Be(JobMode.FetchLane);

            using (var inner = context.BeginIteration(JobMode.AggregateLane))
            {
                context.CurrentJobMode.Should().Be(JobMode.AggregateLane);
                inner.IterationId.Should().NotBe(outer.IterationId);
            }

            context.CurrentJobMode.Should().Be(JobMode.FetchLane);
        }

        context.CurrentJobMode.Should().BeNull();
    }
}
