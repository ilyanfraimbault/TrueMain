using Data;
using Microsoft.EntityFrameworkCore;

namespace Ingestor.Processes.Components.PatternAggregation;

/// <summary>
/// Keeps <c>champion_patch_stats</c> — the light per-patch record (#1956) — equal to
/// <c>champion_aggregate_scopes</c> summed over the account and the platform. The pattern
/// aggregation is the only writer of the scopes, so folding its replacements is enough: a
/// batch's slices are subtracted (<see cref="SubtractSlicesAsync"/>) before they are deleted
/// and added back (<see cref="AddSlicesAsync"/>) once their new scopes are in, inside the
/// same transaction, and a row a replacement emptied is dropped. Retention deletes scopes without touching the
/// record, which is the point: it is the history of a purged patch.
/// </summary>
internal static class ChampionPatchStatFold
{
    /// <summary>
    /// The backfill is one grouped pass over the whole scope table, which on production is
    /// longer than the connection's 300 s is meant for — the same allowance as
    /// <c>ChampionGameCountRefreshProcess</c>.
    /// </summary>
    internal static readonly TimeSpan BackfillCommandTimeout = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Fills an empty record from every scope present, once, before the first fold. The
    /// fold is only exact over a record that already holds the scopes it replaces, so this
    /// runs at the start of every aggregation run and does nothing once the table has a
    /// row: in production that is the backfill of every patch whose full aggregates still
    /// exist, on a fresh database a no-op until the first fold writes.
    /// </summary>
    internal static async Task<int> BackfillIfEmptyAsync(
        TrueMainDbContext db,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
    {
        if (await db.ChampionPatchStats.AsNoTracking().AnyAsync(ct))
        {
            return 0;
        }

        db.Database.SetCommandTimeout(BackfillCommandTimeout);
        return await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "champion_patch_stats"
                ("QueueId", "Patch", "ChampionId", "Position", "EloBracket", "IsMain", "Games", "Wins", "AggregatedAtUtc")
            SELECT s."QueueId", s."GameVersion", s."ChampionId", s."Position", s."elo_bracket", s."IsMain",
                SUM(s."Games")::int, SUM(s."Wins")::int, {aggregatedAtUtc}
            FROM "champion_aggregate_scopes" AS s
            GROUP BY s."QueueId", s."GameVersion", s."ChampionId", s."Position", s."elo_bracket", s."IsMain"
            """,
            ct);
    }

    /// <summary>
    /// Takes the stored scopes of <paramref name="slices"/> out of the record, before they
    /// are deleted. An <c>UPDATE</c> rather than an upsert: a key the record lacks has
    /// nothing to subtract from, and inserting it negative would break the row's
    /// wins-within-games check — <see cref="AddSlicesAsync"/> then writes it whole. For the
    /// same reason the result is floored at zero: over a record that agrees with the scopes
    /// the floor never applies, and over one that drifted (scopes edited by hand) it lets
    /// the replacement write the slice back instead of failing every run on the check.
    /// </summary>
    internal static Task SubtractSlicesAsync(
        TrueMainDbContext db,
        int queueId,
        SliceKeyColumns slices,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
        => db.Database.ExecuteSqlAsync(
            $"""
            UPDATE "champion_patch_stats" AS t
            SET "Games" = GREATEST(t."Games" - d."Games", 0),
                "Wins" = LEAST(GREATEST(t."Wins" - d."Wins", 0), GREATEST(t."Games" - d."Games", 0)),
                "AggregatedAtUtc" = {aggregatedAtUtc}
            FROM (
                SELECT s."QueueId", s."GameVersion", s."ChampionId", s."Position", s."elo_bracket", s."IsMain",
                    SUM(s."Games")::int AS "Games", SUM(s."Wins")::int AS "Wins"
                FROM "champion_aggregate_scopes" AS s
                INNER JOIN (
                    SELECT DISTINCT * FROM unnest({slices.RiotAccountIds}, {slices.ChampionIds}, {slices.GameVersions}, {slices.PlatformIds})
                        AS u("RiotAccountId", "ChampionId", "GameVersion", "PlatformId")
                ) AS k
                    ON s."RiotAccountId" = k."RiotAccountId"
                    AND s."ChampionId" = k."ChampionId"
                    AND s."GameVersion" = k."GameVersion"
                    AND s."PlatformId" = k."PlatformId"
                WHERE s."QueueId" = {queueId}
                GROUP BY s."QueueId", s."GameVersion", s."ChampionId", s."Position", s."elo_bracket", s."IsMain"
            ) AS d
            WHERE t."QueueId" = d."QueueId"
                AND t."Patch" = d."GameVersion"
                AND t."ChampionId" = d."ChampionId"
                AND t."Position" = d."Position"
                AND t."EloBracket" = d."elo_bracket"
                AND t."IsMain" = d."IsMain"
            """,
            ct);

    /// <summary>
    /// Adds the stored scopes of <paramref name="slices"/> to the record, once their
    /// replacement is in. The slice keys are de-duplicated first, here and in
    /// <see cref="SubtractSlicesAsync"/>: unlike the delete they pair with, a sum would
    /// count a scope twice for a key listed twice.
    /// </summary>
    internal static Task AddSlicesAsync(
        TrueMainDbContext db,
        int queueId,
        SliceKeyColumns slices,
        DateTime aggregatedAtUtc,
        CancellationToken ct)
        => db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "champion_patch_stats" AS t
                ("QueueId", "Patch", "ChampionId", "Position", "EloBracket", "IsMain", "Games", "Wins", "AggregatedAtUtc")
            SELECT s."QueueId", s."GameVersion", s."ChampionId", s."Position", s."elo_bracket", s."IsMain",
                SUM(s."Games")::int, SUM(s."Wins")::int, {aggregatedAtUtc}
            FROM "champion_aggregate_scopes" AS s
            INNER JOIN (
                SELECT DISTINCT * FROM unnest({slices.RiotAccountIds}, {slices.ChampionIds}, {slices.GameVersions}, {slices.PlatformIds})
                    AS u("RiotAccountId", "ChampionId", "GameVersion", "PlatformId")
            ) AS k
                ON s."RiotAccountId" = k."RiotAccountId"
                AND s."ChampionId" = k."ChampionId"
                AND s."GameVersion" = k."GameVersion"
                AND s."PlatformId" = k."PlatformId"
            WHERE s."QueueId" = {queueId}
            GROUP BY s."QueueId", s."GameVersion", s."ChampionId", s."Position", s."elo_bracket", s."IsMain"
            ON CONFLICT ("QueueId", "Patch", "Position", "EloBracket", "IsMain", "ChampionId") DO UPDATE SET
                "Games" = t."Games" + EXCLUDED."Games",
                "Wins" = t."Wins" + EXCLUDED."Wins",
                "AggregatedAtUtc" = EXCLUDED."AggregatedAtUtc"
            """,
            ct);

    /// <summary>
    /// Drops the rows of the folded (patch, champion) pairs a replacement emptied — a lane
    /// or a bracket the slices no longer play in — so the record never holds a 0-game row
    /// the scopes do not.
    /// </summary>
    internal static Task PruneEmptiedAsync(
        TrueMainDbContext db,
        int queueId,
        SliceKeyColumns slices,
        CancellationToken ct)
        => db.Database.ExecuteSqlAsync(
            $"""
            DELETE FROM "champion_patch_stats" AS t
            USING (SELECT DISTINCT * FROM unnest({slices.GameVersions}, {slices.ChampionIds}) AS u("Patch", "ChampionId")) AS k
            WHERE t."QueueId" = {queueId}
                AND t."Patch" = k."Patch"
                AND t."ChampionId" = k."ChampionId"
                AND t."Games" = 0
            """,
            ct);
}

/// <summary>A batch of <see cref="AggregateSliceKey"/>s as the parallel arrays an <c>unnest</c> takes.</summary>
internal sealed record SliceKeyColumns(Guid[] RiotAccountIds, int[] ChampionIds, string[] GameVersions, string[] PlatformIds)
{
    public static SliceKeyColumns From(IReadOnlyCollection<AggregateSliceKey> slices)
        => new(
            slices.Select(slice => slice.RiotAccountId).ToArray(),
            slices.Select(slice => slice.ChampionId).ToArray(),
            slices.Select(slice => slice.GameVersion).ToArray(),
            slices.Select(slice => slice.PlatformId).ToArray());
}
