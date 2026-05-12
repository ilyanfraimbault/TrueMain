namespace Ingestor.Processes;

public interface IIngestorProcess
{
    string Name { get; }

    /// <summary>
    /// Executes the process logic and returns a serialisable summary payload that
    /// the surrounding <c>RecordedProcess</c> decorator persists via the recorder.
    /// Implementations must NOT call <c>IProcessRunRecorder</c> themselves.
    /// </summary>
    Task<object?> RunCoreAsync(CancellationToken ct);
}
