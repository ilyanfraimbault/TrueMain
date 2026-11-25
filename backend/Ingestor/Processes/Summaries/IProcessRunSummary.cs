using Data.Entities;

namespace Ingestor.Processes.Summaries;

/// <summary>
/// Marker for the payload an <see cref="IIngestorProcess"/> returns and the
/// recorder persists into <c>process_runs.summary</c>.
/// </summary>
/// <remarks>
/// Summaries used to be anonymous types, which forced reflection-based
/// serialization on every run and made the persisted shape impossible to
/// reference from a <c>JsonSerializerContext</c>. Closing the set behind this
/// marker means every payload is a named type that can be registered in
/// <see cref="ProcessRunSummaryJsonContext"/> — see
/// <c>ProcessRunSummaryRegistrationTests</c>, which fails the build when a new
/// implementation is added without registering it.
/// </remarks>
public interface IProcessRunSummary
{
    /// <summary>
    /// The status a completed run is recorded under. A run that did its work, even with nothing
    /// to do, is <see cref="ProcessRunStatus.Success"/>; only a summary that declined the work
    /// (<see cref="SkippedSummary"/>) says otherwise. Not serialized: the status has its own field.
    /// </summary>
    ProcessRunStatus Status => ProcessRunStatus.Success;
}
