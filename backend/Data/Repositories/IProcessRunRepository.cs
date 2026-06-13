using Data.Entities;

namespace Data.Repositories;

public interface IProcessRunRepository
{
    void Add(ProcessRun run);

    /// <summary>
    /// Loads a single run by id (tracked), or <see langword="null"/> if none
    /// exists. Used to flip a <c>Running</c> row to its terminal
    /// <c>Success</c>/<c>Failed</c> state on completion.
    /// </summary>
    Task<ProcessRun?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Loads every run still in <c>Running</c> (tracked) so the caller can flip
    /// orphaned in-flight rows to <c>Abandoned</c>. Used by startup reconciliation
    /// on the single-instance ingestor, where anything still <c>Running</c> at boot
    /// is by definition orphaned.
    /// </summary>
    Task<IReadOnlyList<ProcessRun>> GetRunningAsync(CancellationToken ct);
}
