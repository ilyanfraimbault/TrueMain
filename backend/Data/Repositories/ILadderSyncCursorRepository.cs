using Data.Entities;

namespace Data.Repositories;

public interface ILadderSyncCursorRepository
{
    /// <summary>The platform's sweep position, or null when it has never swept.</summary>
    Task<LadderSyncCursor?> GetAsync(string platformId, CancellationToken ct);

    /// <summary>
    /// Insert or update the platform's sweep position. Written immediately with a single
    /// statement, so it does not need (and is not affected by) a later SaveChanges.
    /// </summary>
    Task UpsertAsync(string platformId, string tier, string division, int page, DateTime nowUtc, CancellationToken ct);
}
