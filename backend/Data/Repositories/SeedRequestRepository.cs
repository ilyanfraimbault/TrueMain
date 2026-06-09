using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class SeedRequestRepository(TrueMainDbContext db) : ISeedRequestRepository
{
    public Task<List<SeedRequest>> GetPendingAsync(int batchSize, CancellationToken ct)
        => db.SeedRequests
            // Tracked (no AsNoTracking): the process flips Status to Resolving and
            // later to Ingested/Failed on these same instances, then SaveChanges.
            .Where(request => request.Status == SeedRequestStatus.Pending)
            // Oldest-first so the backlog drains fairly (FIFO).
            .OrderBy(request => request.RequestedAtUtc)
            .ThenBy(request => request.Id)
            .Take(batchSize)
            .ToListAsync(ct);

    public Task<SeedRequest?> GetByIdAsync(Guid id, CancellationToken ct)
        // Tracked: callers transition Status on the returned instance.
        => db.SeedRequests.FirstOrDefaultAsync(request => request.Id == id, ct);
}
