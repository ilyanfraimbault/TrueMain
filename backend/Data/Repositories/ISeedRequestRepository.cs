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

    /// <summary>
    /// Atomically claims a request by flipping it from <c>Pending</c> to
    /// <c>Resolving</c> in a single <c>UPDATE … WHERE Status = Pending</c>.
    /// Returns the number of rows affected (1 if this caller won the claim,
    /// 0 if it was already claimed/changed by a concurrent run or has
    /// vanished). This closes the read-then-write TOCTOU window two concurrent
    /// runs would otherwise share.
    /// </summary>
    Task<int> ClaimAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Resets a request from <c>Resolving</c> back to <c>Pending</c> so a later
    /// run can re-claim it. Used when a claimed request is interrupted (host
    /// shutdown / cancellation) before reaching a terminal state, which would
    /// otherwise strand it forever. No-op if the row is no longer
    /// <c>Resolving</c>.
    /// </summary>
    Task<int> ResetResolvingToPendingAsync(Guid id, CancellationToken ct);
}
