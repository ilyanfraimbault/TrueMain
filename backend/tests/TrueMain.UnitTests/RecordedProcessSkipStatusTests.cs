using AwesomeAssertions;
using Data.Entities;
using Ingestor.Processes;
using Ingestor.Processes.Summaries;
using Ingestor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// A run that a cadence guard declined must be recorded as
/// <see cref="ProcessRunStatus.Skipped"/>, not <see cref="ProcessRunStatus.Success"/> (#1149).
///
/// <para>
/// This is the half of the deadlock that lives in the writer. A cadence guard measures its
/// interval from the last run that actually did its work, so as long as a skip was written
/// as a success it stood in for the work it had just declined: the next iteration read a
/// "completed run" minutes old, skipped again, and wrote another success. Ladder discovery
/// ran exactly once per process-run store and then never again for two months. The reader
/// half — the query excluding these rows — is pinned by
/// <c>ProcessRunCadenceQueryIntegrationTests</c>.
/// </para>
/// </summary>
public sealed class RecordedProcessSkipStatusTests
{
    [Fact]
    public async Task RunCoreAsync_WhenTheInnerProcessSkipped_RecordsSkippedRatherThanSuccess()
    {
        var recorder = Substitute.For<IProcessRunRecorder>();
        var process = Build(recorder, () => new SkippedSummary("Within MinRunInterval.", true));

        await process.RunCoreAsync(CancellationToken.None);

        await recorder.Received(1).RecordAsync(
            Arg.Any<Guid>(),
            "Stub",
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            ProcessRunStatus.Skipped,
            Arg.Any<IProcessRunSummary>(),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCoreAsync_WhenTheInnerProcessDidWork_StillRecordsSuccess()
    {
        var recorder = Substitute.For<IProcessRunRecorder>();
        var process = Build(recorder, () => new MatchAggregationSummary(3, 1));

        await process.RunCoreAsync(CancellationToken.None);

        await recorder.Received(1).RecordAsync(
            Arg.Any<Guid>(),
            "Stub",
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            ProcessRunStatus.Success,
            Arg.Any<IProcessRunSummary>(),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCoreAsync_WhenTheProcessHadNothingToDo_RecordsSuccessNotSkipped()
    {
        var recorder = Substitute.For<IProcessRunRecorder>();
        var process = Build(recorder, () => new NoWorkSummary("Nothing selected.", 0));

        await process.RunCoreAsync(CancellationToken.None);

        // "Nothing to do" is not "did not look": the process ran, found an empty input and
        // finished. It must keep counting as a real run so a cadence guard measures its
        // interval from it — only a guard-declined iteration is a skip.
        await recorder.Received(1).RecordAsync(
            Arg.Any<Guid>(),
            "Stub",
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            ProcessRunStatus.Success,
            Arg.Any<IProcessRunSummary>(),
            null,
            Arg.Any<CancellationToken>());
    }

    private static RecordedProcess<StubProcess> Build(
        IProcessRunRecorder recorder,
        Func<IProcessRunSummary?> payload)
        => new(
            new StubProcess(payload),
            recorder,
            TimeProvider.System,
            NullLogger<RecordedProcess<StubProcess>>.Instance);

    private sealed class StubProcess(Func<IProcessRunSummary?> payload) : IIngestorProcess
    {
        public string Name => "Stub";

        public Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
            => Task.FromResult(payload());
    }
}
