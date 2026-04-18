using Data.Entities;
using Ingestor.Services;

namespace TrueMain.TestKit;

/// <summary>
/// No-op <see cref="IProcessRunRecorder"/> for tests that instantiate a
/// <see cref="Ingestor.Processes.RecordedProcess{TInner}"/> but don't care
/// about the persisted process run. Prefer a real <see cref="ProcessRunRecorder"/>
/// when the assertion checks the <c>ProcessRuns</c> table.
/// </summary>
public sealed class FakeProcessRunRecorder : IProcessRunRecorder
{
    public Task RecordAsync(
        string processName,
        DateTime startedAtUtc,
        DateTime finishedAtUtc,
        ProcessRunStatus status,
        object? summary,
        string? error,
        CancellationToken ct)
        => Task.CompletedTask;
}
