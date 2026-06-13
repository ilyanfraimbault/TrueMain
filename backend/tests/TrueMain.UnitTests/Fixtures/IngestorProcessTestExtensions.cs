using Ingestor.Processes;
using Ingestor.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace TrueMain.UnitTests.Fixtures;

internal static class IngestorProcessTestExtensions
{
    /// <summary>
    /// Wraps the process in <see cref="RecordedProcess{TInner}"/> so tests that
    /// assert on the recorder keep the same semantics they had before
    /// the recorder was lifted out of every *Process.
    /// </summary>
    public static Task<object?> RunRecordedAsync<T>(
        this T process,
        IProcessRunRecorder recorder,
        CancellationToken ct = default)
        where T : IIngestorProcess
        => new RecordedProcess<T>(process, recorder, NullLogger<RecordedProcess<T>>.Instance).RunCoreAsync(ct);
}
