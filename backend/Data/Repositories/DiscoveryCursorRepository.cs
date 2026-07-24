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

    /// <remarks>
    /// A single parameterised INSERT … ON CONFLICT (#500): the previous tracked
    /// read + Add/mutate re-queried the row that <see cref="GetOffsetAsync"/> had
    /// already read with <c>AsNoTracking</c> (EF cannot reuse a no-tracking result),
    /// so the write cost two reads. "PlatformId" is the table's primary key, which
    /// is the conflict target here. Nothing tracks this entity — the only read is
    /// no-tracking — so bypassing the change tracker leaves no stale instance behind.
    /// </remarks>
    public Task UpsertOffsetAsync(string platformId, int offset, DateTime nowUtc, CancellationToken ct)
        => db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "discovery_cursors" ("PlatformId", "Offset", "UpdatedAtUtc")
            VALUES ({platformId}, {offset}, {nowUtc})
            ON CONFLICT ("PlatformId") DO UPDATE
                SET "Offset" = EXCLUDED."Offset",
                    "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
            """,
            ct);
}
