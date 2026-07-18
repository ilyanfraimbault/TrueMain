using AwesomeAssertions;
using Data.Entities;
using Data.Logging;
using Ingestor.Processes;
using Ingestor.Services;
using Microsoft.Extensions.Logging;

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
            new StubProcess(() => new { rows = 3 }),
            new NoOpRecorder(),
            TimeProvider.System,
            logger);

        await process.RunCoreAsync(CancellationToken.None);

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
            new NoOpRecorder(),
            TimeProvider.System,
            logger);

        var act = async () => await process.RunCoreAsync(CancellationToken.None);
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
            object? summary,
            string? error,
            CancellationToken ct)
            => Task.CompletedTask;

        public Task HeartbeatAsync(Guid runId, CancellationToken ct)
            => Task.CompletedTask;

        public Task<int> ReconcileOrphanedRunsAsync(CancellationToken ct)
            => Task.FromResult(0);
    }

    private sealed class StubProcess(Func<object?> body) : IIngestorProcess
    {
        public string Name => "Stub";

        public Task<object?> RunCoreAsync(CancellationToken ct) => Task.FromResult(body());
    }

    private sealed record LogEntry(LogLevel Level, EventId EventId, string Message, Exception? Exception);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
    }
}
