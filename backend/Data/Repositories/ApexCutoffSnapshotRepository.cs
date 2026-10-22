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
            INSERT INTO "apex_cutoff_snapshots" ("PlatformId", "Tier", "CapturedOnUtc", "MinLeaguePoints", "EntryCount", "UpdatedAtUtc", "IsEstimated")
            VALUES ({platformId}, {tier.ToRiotName()}, {day}, {minLeaguePoints}, {entryCount}, {nowUtc}, FALSE)
            ON CONFLICT ("PlatformId", "Tier", "CapturedOnUtc") DO UPDATE
                SET "MinLeaguePoints" = EXCLUDED."MinLeaguePoints",
                    "EntryCount" = EXCLUDED."EntryCount",
                    "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc",
                    "IsEstimated" = FALSE
            """,
            ct);
    }

    /// <summary>Fills the days before the first measurement with an estimate, once.</summary>
    /// <remarks>
    /// The estimate of a day is the lowest LP among that day's Grandmaster / Challenger rows of
    /// <c>rank_snapshots</c>: a tracked account keeps one row per UTC day (its last standing of the
    /// day, <c>RankSnapshotWriter</c>), and a player still in the tier at the end of the day sits
    /// at or above its cutoff, so the lowest of them is the cutoff seen through the sample we
    /// track — exact when the sample holds the league's last player, above it otherwise. Days with
    /// fewer than <paramref name="minSnapshots"/> rows are left out rather than guessed. One
    /// statement: <c>ON CONFLICT DO NOTHING</c> leaves every measured day as measured, and the
    /// <c>NOT EXISTS</c> guard makes every run after the first a no-op without scanning
    /// <c>rank_snapshots</c> again.
    /// </remarks>
    public Task<int> EstimateHistoryAsync(DateTime fromUtc, DateTime nowUtc, int minSnapshots, CancellationToken ct)
    {
        var todayUtc = nowUtc.Date;
        return db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "apex_cutoff_snapshots" ("PlatformId", "Tier", "CapturedOnUtc", "MinLeaguePoints", "EntryCount", "UpdatedAtUtc", "IsEstimated")
            SELECT a."PlatformId", s."Tier", (s."CapturedAtUtc" AT TIME ZONE 'UTC')::date, min(s."LeaguePoints"), count(*), {nowUtc}, TRUE
            FROM "rank_snapshots" s
            JOIN "riot_accounts" a ON a."Id" = s."RiotAccountId"
            WHERE s."Tier" IN ('GRANDMASTER', 'CHALLENGER')
              AND s."CapturedAtUtc" >= {fromUtc}
              AND s."CapturedAtUtc" < {todayUtc}
              AND NOT EXISTS (SELECT 1 FROM "apex_cutoff_snapshots" e WHERE e."IsEstimated")
            GROUP BY a."PlatformId", s."Tier", (s."CapturedAtUtc" AT TIME ZONE 'UTC')::date
            HAVING count(*) >= {minSnapshots}
            ON CONFLICT ("PlatformId", "Tier", "CapturedOnUtc") DO NOTHING
            """,
            ct);
    }
}
