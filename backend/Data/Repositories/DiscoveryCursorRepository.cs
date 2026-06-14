using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class DiscoveryCursorRepository(TrueMainDbContext db) : IDiscoveryCursorRepository
{
    public async Task<int?> GetOffsetAsync(string platformId, CancellationToken ct)
    {
        var cursor = await db.DiscoveryCursors
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.PlatformId == platformId, ct);
        return cursor?.Offset;
    }

    public async Task UpsertOffsetAsync(string platformId, int offset, DateTime nowUtc, CancellationToken ct)
    {
        var cursor = await db.DiscoveryCursors.FirstOrDefaultAsync(c => c.PlatformId == platformId, ct);
        if (cursor is null)
        {
            db.DiscoveryCursors.Add(new DiscoveryCursor
            {
                PlatformId = platformId,
                Offset = offset,
                UpdatedAtUtc = nowUtc
            });
            return;
        }

        cursor.Offset = offset;
        cursor.UpdatedAtUtc = nowUtc;
    }
}
