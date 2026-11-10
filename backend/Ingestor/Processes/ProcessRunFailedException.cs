using Ingestor.Processes.Summaries;

namespace Ingestor.Processes;

/// <summary>
/// A run that went through its work but failed as a whole — every Riot call it made failed,
/// say — and still reports what it did. The <see cref="RecordedProcess{TInner}"/> records it
/// Failed <em>with</em> <see cref="Summary"/>, so the failure is visible and alertable while the
/// calls it spent are still charged to the daily budgets that read summaries back.
/// </summary>
public sealed class ProcessRunFailedException(string message, IProcessRunSummary summary) : Exception(message)
{
    /// <summary>What the failed run did.</summary>
    public IProcessRunSummary Summary { get; } = summary;
}
