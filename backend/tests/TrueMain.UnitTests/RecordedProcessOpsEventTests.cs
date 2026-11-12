using AwesomeAssertions;
using Data.Entities;
using Data.Logging;
using Ingestor.Processes;
using Ingestor.Processes.Summaries;
using Ingestor.Services;
using Microsoft.Extensions.Logging;
using TrueMain.UnitTests.Fixtures;
using Data.Ops.Mongo;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// The <see cref="RecordedProcess{TInner}"/> decorator emits the
/// <see cref="OpsEvents.ProcessRunCompleted"/> / <see cref="OpsEvents.ProcessRunFailed"/>
/// ops events (#722) so every pipeline step is visible and filterable in the
/// admin Logs panel. These tests pin that the events actually fire, with the
/// registered event ids (what the Mongo sink keys on) and the right levels.
/// </summary>
public sealed class RecordedProcessOpsEventTests
{
    [Fact]
    public async Task RunCoreAsync_OnSuccess_EmitsProcessRunCompletedEvent()
    {
        var logger = new CapturingLogger<RecordedProcess<StubProcess>>();
        var process = new RecordedProcess<StubProcess>(
            new StubProcess(() => new EloBracketEnrichmentSummary(3, 1, 1)),
            TestPipelineSteps.Named("Stub"),
            new NoOpRecorder(),
            Substitute.For<IProcessRunStore>(),
            TimeProvider.System,
            logger);

        await process.RunCoreAsync(RunInput.Whole, CancellationToken.None);

        var entry = logger.Entries.Should().ContainSingle(
            e => e.EventId.Id == OpsEvents.ProcessRunCompleted.Id).Subject;
        entry.Level.Should().Be(LogLevel.Information);
        entry.EventId.Name.Should().Be(nameof(OpsEvents.ProcessRunCompleted));
        // The event must resolve through the registered catalog — that's what
        // makes the Mongo sink persist it below its Warning floor.
        OpsEvents.Resolve(entry.EventId).Should().Be(nameof(OpsEvents.ProcessRunCompleted));
        entry.Message.Should().Contain("Stub");
    }

    [Fact]
    public async Task RunCoreAsync_OnFailure_EmitsProcessRunFailedEventAndRethrows()
    {
        var logger = new CapturingLogger<RecordedProcess<StubProcess>>();
        var boom = new InvalidOperationException("boom");
        var process = new RecordedProcess<StubProcess>(
            new StubProcess(() => throw boom),
            TestPipelineSteps.Named("Stub"),
            new NoOpRecorder(),
            Substitute.For<IProcessRunStore>(),
            TimeProvider.System,
            logger);

        var act = async () => await process.RunCoreAsync(RunInput.Whole, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        var entry = logger.Entries.Should().ContainSingle(
            e => e.EventId.Id == OpsEvents.ProcessRunFailed.Id).Subject;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Exception.Should().BeSameAs(boom);
        OpsEvents.Resolve(entry.EventId).Should().Be(nameof(OpsEvents.ProcessRunFailed));
        logger.Entries.Should().NotContain(e => e.EventId.Id == OpsEvents.ProcessRunCompleted.Id);
    }

    private sealed class NoOpRecorder : IProcessRunRecorder
    {
        public Task<Guid> RecordStartAsync(string processName, DateTime startedAtUtc, CancellationToken ct)
            => Task.FromResult(Guid.NewGuid());

        public Task RecordAsync(
            Guid runId,
            string processName,
            DateTime startedAtUtc,
            DateTime finishedAtUtc,
            ProcessRunStatus status,
            IProcessRunSummary? summary,
            string? error,
            CancellationToken ct)
            => Task.CompletedTask;

        public Task HeartbeatAsync(Guid runId, CancellationToken ct)
            => Task.CompletedTask;

        public Task<int> ReconcileOrphanedRunsAsync(IReadOnlyCollection<string> processNames, CancellationToken ct)
            => Task.FromResult(0);
    }

    private sealed class StubProcess(Func<IProcessRunSummary?> body) : IIngestorProcess
    {
        public Task<IProcessRunSummary?> RunCoreAsync(RunInput input, CancellationToken ct) => Task.FromResult(body());
    }
}
