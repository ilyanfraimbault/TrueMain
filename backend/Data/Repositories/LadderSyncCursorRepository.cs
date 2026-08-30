using Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class LadderSyncCursorRepository(TrueMainDbContext db) : ILadderSyncCursorRepository
{
    public Task<LadderSyncCursor?> GetAsync(string platformId, CancellationToken ct)
        => db.LadderSyncCursors
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.PlatformId == platformId, ct);

    /// <remarks>
    /// A single parameterised INSERT … ON CONFLICT, for the same reason as
    /// <see cref="DiscoveryCursorRepository.UpsertOffsetAsync"/> (#500): the read above is
    /// <c>AsNoTracking</c>, which EF cannot reuse for a tracked write, so going through the
    /// change tracker would cost a second read. "PlatformId" is the table's primary key and
    /// therefore the conflict target. Nothing tracks this entity, so bypassing the tracker
    /// leaves no stale instance behind.
    /// </remarks>
    public Task UpsertAsync(string platformId, string tier, string division, int page, DateTime nowUtc, CancellationToken ct)
        => db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "ladder_sync_cursors" ("PlatformId", "Tier", "Division", "Page", "UpdatedAtUtc")
            VALUES ({platformId}, {tier}, {division}, {page}, {nowUtc})
            ON CONFLICT ("PlatformId") DO UPDATE
                SET "Tier" = EXCLUDED."Tier",
                    "Division" = EXCLUDED."Division",
                    "Page" = EXCLUDED."Page",
                    "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
            """,
            ct);
}
