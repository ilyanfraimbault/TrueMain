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
}
