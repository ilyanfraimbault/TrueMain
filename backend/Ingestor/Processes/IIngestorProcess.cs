using Ingestor.Processes.Summaries;

namespace Ingestor.Processes;

/// <summary>
/// One pipeline step. The class carries <see cref="PipelineStepAttribute"/> naming its
/// <c>Data.Pipeline.PipelineCatalog</c> entry, which is how it is registered and recorded.
/// </summary>
public interface IIngestorProcess
{
    /// <summary>
    /// Executes the process logic and returns a serialisable summary payload that
    /// the surrounding <c>RecordedProcess</c> decorator persists via the recorder.
    /// Implementations must NOT call <c>IProcessRunRecorder</c> themselves.
    /// </summary>
    Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct);
}
