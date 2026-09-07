using AwesomeAssertions;
using Data.Entities;
using Ingestor.Processes;
using Ingestor.Processes.Summaries;
using Ingestor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TrueMain.UnitTests;

/// <summary>
/// A run an orderly shutdown cuts short is recorded as
/// <see cref="ProcessRunStatus.Cancelled"/> before the cancellation propagates (#1513).
///
/// <para>
/// It used to be recorded as nothing at all: the Running row was left behind for the next
/// boot's reconciliation to age out to <see cref="ProcessRunStatus.Abandoned"/>, so a
/// redeploy and a host that died left the same trace — and for as long as the container
/// stayed down, the panels showed a run still in flight inside a process that no longer
/// existed.
/// </para>
/// </summary>
public sealed class RecordedProcessCancellationTests
{
    [Fact]
    public async Task RunCoreAsync_WhenTheShutdownCancelsTheRun_RecordsCancelledAndRethrows()
    {
        var recorder = Substitute.For<IProcessRunRecorder>();
        using var shutdown = new CancellationTokenSource();
        var process = Build(recorder, () =>
        {
            shutdown.Cancel();
            shutdown.Token.ThrowIfCancellationRequested();
            return null;
        });

        var act = async () => await process.RunCoreAsync(shutdown.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "cancellation is the host's business and must keep propagating to the worker");

        await recorder.Received(1).RecordAsync(
            Arg.Any<Guid>(),
            "Stub",
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            ProcessRunStatus.Cancelled,
            null,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        await recorder.DidNotReceive().RecordAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            ProcessRunStatus.Failed,
            Arg.Any<IProcessRunSummary?>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunCoreAsync_WhenTheShutdownCancelsTheRun_WritesTheOutcomeThroughAnUncancelledToken()
    {
        // The whole reason the status can be written at all: the run's own token is
        // cancelled by the time this code runs, so reusing it would throw a second
        // cancellation from inside the store call and record nothing.
        var recorder = Substitute.For<IProcessRunRecorder>();
        var tokens = new List<CancellationToken>();
        recorder
            .RecordAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                Arg.Any<ProcessRunStatus>(),
                Arg.Any<IProcessRunSummary?>(),
                Arg.Any<string>(),
                Arg.Do<CancellationToken>(tokens.Add))
            .Returns(Task.CompletedTask);

        using var shutdown = new CancellationTokenSource();
        var process = Build(recorder, () =>
        {
            shutdown.Cancel();
            shutdown.Token.ThrowIfCancellationRequested();
            return null;
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await process.RunCoreAsync(shutdown.Token));

        tokens.Should().ContainSingle().Which.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task RunCoreAsync_WhenRecordingTheCancellationFails_StillRethrowsTheCancellation()
    {
        // Best-effort by design: the host is already stopping, and the fallback is the
        // behaviour this replaced — startup reconciliation settles the row.
        var recorder = Substitute.For<IProcessRunRecorder>();
        recorder
            .RecordAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<DateTime>(),
                Arg.Any<DateTime>(),
                ProcessRunStatus.Cancelled,
                Arg.Any<IProcessRunSummary?>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new TimeoutException("store unreachable")));

        using var shutdown = new CancellationTokenSource();
        var process = Build(recorder, () =>
        {
            shutdown.Cancel();
            shutdown.Token.ThrowIfCancellationRequested();
            return null;
        });

        var act = async () => await process.RunCoreAsync(shutdown.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static RecordedProcess<StubProcess> Build(
        IProcessRunRecorder recorder,
        Func<IProcessRunSummary?> body)
        => new(
            new StubProcess(body),
            recorder,
            TimeProvider.System,
            NullLogger<RecordedProcess<StubProcess>>.Instance);

    private sealed class StubProcess(Func<IProcessRunSummary?> body) : IIngestorProcess
    {
        public string Name => "Stub";

        public Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct) => Task.FromResult(body());
    }
}
