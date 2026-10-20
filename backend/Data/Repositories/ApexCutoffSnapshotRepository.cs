using Core.Lol.Ranking;
using Microsoft.EntityFrameworkCore;

namespace Data.Repositories;

public sealed class ApexCutoffSnapshotRepository(TrueMainDbContext db) : IApexCutoffSnapshotRepository
{
    /// <summary>Stores the day's cutoff for the platform and tier, replacing an earlier read of the same day.</summary>
    /// <remarks>
    /// A single parameterised INSERT … ON CONFLICT on the (platform, tier, day) key, like
    /// <see cref="LadderSyncCursorRepository.UpsertAsync"/>: the day's latest read wins, and
    /// nothing tracks this entity, so bypassing the change tracker leaves no stale instance.
    /// </remarks>
    public Task UpsertAsync(string platformId, RankTier tier, int minLeaguePoints, int entryCount, DateTime nowUtc, CancellationToken ct)
    {
        var day = DateOnly.FromDateTime(nowUtc);
        return db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "apex_cutoff_snapshots" ("PlatformId", "Tier", "CapturedOnUtc", "MinLeaguePoints", "EntryCount", "UpdatedAtUtc")
            VALUES ({platformId}, {tier.ToRiotName()}, {day}, {minLeaguePoints}, {entryCount}, {nowUtc})
            ON CONFLICT ("PlatformId", "Tier", "CapturedOnUtc") DO UPDATE
                SET "MinLeaguePoints" = EXCLUDED."MinLeaguePoints",
                    "EntryCount" = EXCLUDED."EntryCount",
                    "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
            """,
            ct);
    }
}
