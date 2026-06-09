using Data.Entities;

namespace Data.Repositories;

public interface ISeedRequestRepository
{
    /// <summary>
    /// Loads up to <paramref name="batchSize"/> oldest <c>Pending</c> seed
    /// requests (tracked, so the caller can transition them) for the Ingestor's
    /// ManualSeedProcess to claim and resolve.
    /// </summary>
    Task<List<SeedRequest>> GetPendingAsync(int batchSize, CancellationToken ct);

    /// <summary>
    /// Loads a single seed request by id (tracked), or <see langword="null"/> if
    /// none exists. Used to (re)claim a request inside its own session and to
    /// record the terminal state.
    /// </summary>
    Task<SeedRequest?> GetByIdAsync(Guid id, CancellationToken ct);
}
