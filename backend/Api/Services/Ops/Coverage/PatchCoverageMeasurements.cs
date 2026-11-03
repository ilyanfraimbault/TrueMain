using Core.Lol.Patches;
using Data;
using Data.Aggregation;
using Microsoft.EntityFrameworkCore;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Coverage;

/// <summary>
/// The two per-patch reads the patch-coverage verdict rests on (#1033): ingestion
/// (grouped over <c>matches</c>) and aggregate coverage (grouped over
/// <c>champion_aggregate_scopes</c> on the champion directory's grain and floor).
/// Measurement only — the judgement is <see cref="PatchCoverageEvaluator"/>'s and the
/// wording is <see cref="PatchCoverageRows"/>'.
/// </summary>
internal sealed class PatchCoverageMeasurements(TrueMainDbContext db)
{
    /// <summary>
    /// Every distinct <c>GameVersion</c> on the aggregate side for <paramref name="queueId"/>.
    /// </summary>
    /// <remarks>
    /// A loose index scan (#2302), not a <c>SELECT DISTINCT</c>: the distinct values are a
    /// few dozen and the table holds hundreds of thousands of rows or more, so the plain form
    /// read the whole table to return them. The recursion walks the reader-path index
    /// (<c>ChampionId, GameVersion, …</c>) one <c>(champion, version)</c> pair per probe — a
    /// few thousand index seeks — and the queue is checked per pair afterwards, on the same
    /// index's prefix.
    /// </remarks>
    public async Task<List<string>> LoadScopeVersionsAsync(int queueId, CancellationToken ct)
    {
        FormattableString sql = $"""
            WITH RECURSIVE pairs AS (
                (SELECT s."ChampionId", s."GameVersion"
                 FROM champion_aggregate_scopes s
                 ORDER BY s."ChampionId", s."GameVersion"
                 LIMIT 1)
                UNION ALL
                SELECT next."ChampionId", next."GameVersion"
                FROM pairs
                CROSS JOIN LATERAL (
                    SELECT s."ChampionId", s."GameVersion"
                    FROM champion_aggregate_scopes s
                    WHERE (s."ChampionId", s."GameVersion") > (pairs."ChampionId", pairs."GameVersion")
                    ORDER BY s."ChampionId", s."GameVersion"
                    LIMIT 1) next
            )
            SELECT DISTINCT pairs."GameVersion" AS "Value"
            FROM pairs
            WHERE EXISTS (
                SELECT 1
                FROM champion_aggregate_scopes s
                WHERE s."ChampionId" = pairs."ChampionId"
                  AND s."GameVersion" = pairs."GameVersion"
                  AND s."QueueId" = {queueId})
            """;

        return await db.Database.SqlQuery<string>(sql).ToListAsync(ct);
    }

    /// <summary>
    /// Every distinct patch on the match side for <paramref name="queueId"/>, read from the
    /// database-normalised <c>matches."Patch"</c> column (#1368) — <c>null</c> there is
    /// exactly "does not parse as a patch".
    /// </summary>
    /// <remarks>
    /// A loose index scan over <c>(QueueId, Patch, PlatformId)</c>: one seek per distinct
    /// patch instead of a full read of <c>matches</c> for a dozen values (#2302).
    /// </remarks>
    public async Task<List<string>> LoadMatchPatchesAsync(int queueId, CancellationToken ct)
    {
        FormattableString sql = $"""
            WITH RECURSIVE patches AS (
                (SELECT m."Patch"
                 FROM matches m
                 WHERE m."QueueId" = {queueId} AND m."Patch" IS NOT NULL
                 ORDER BY m."Patch"
                 LIMIT 1)
                UNION ALL
                SELECT (SELECT m."Patch"
                        FROM matches m
                        WHERE m."QueueId" = {queueId} AND m."Patch" > patches."Patch"
                        ORDER BY m."Patch"
                        LIMIT 1)
                FROM patches
                WHERE patches."Patch" IS NOT NULL
            )
            SELECT patches."Patch" AS "Value"
            FROM patches
            WHERE patches."Patch" IS NOT NULL
            """;

        return await db.Database.SqlQuery<string>(sql).ToListAsync(ct);
    }

    /// <summary>
    /// Matches per (patch, game day) on the covered patches, with the per-fold backlog.
    /// </summary>
    /// <param name="queueId">The ranked queue every count is scoped to.</param>
    /// <param name="patches">The covered patches, normalised.</param>
    /// <param name="expectedParticipants">
    /// Participant rows a complete match on the queue carries (its data-quality profile).
    /// Participants are derived as this times the match count, never counted — see
    /// <see cref="PatchCoverageRows.ParticipantsNote"/>.
    /// </param>
    /// <param name="ct">The request's token.</param>
    public async Task<IReadOnlyDictionary<string, PatchIngestion>> LoadIngestionAsync(
        int queueId,
        string[] patches,
        int expectedParticipants,
        CancellationToken ct)
    {
        // Keyed on the stored, database-normalised Patch column — the one the public
        // champion reads filter on (#1368) — rather than re-deriving it from GameVersion.
        FormattableString dailySql = $"""
            SELECT
                m."Patch" AS "Patch",
                to_char((m."GameStartTimeUtc" AT TIME ZONE 'UTC')::date, 'YYYY-MM-DD') AS "Date",
                count(*) AS "Matches",
                min(m."GameStartTimeUtc") AS "FirstGameStartUtc",
                max(m."GameStartTimeUtc") AS "LastGameStartUtc",
                count(*) FILTER (WHERE NOT m."TimelineIngested") AS "PendingTimeline",
                count(*) FILTER (WHERE NOT m."SynergyAggregated") AS "PendingSynergy",
                count(*) FILTER (WHERE NOT m."MatchupLeadAggregated") AS "PendingMatchupLead",
                count(*) FILTER (WHERE NOT m."BansAggregated") AS "PendingBans"
            FROM matches m
            WHERE m."QueueId" = {queueId}
              AND m."Patch" = ANY({patches})
            GROUP BY 1, 2
            """;

        var daily = await db.Database.SqlQuery<PatchDaySqlRow>(dailySql).ToListAsync(ct);

        // No join to match_participants (#2302): it is by far the largest table in the
        // store, and grouping its rows per (patch, day) ran into the statement timeout on
        // every call. Ingestion writes a match together with its whole roster, and a stored
        // match that does not carry it is the wrongParticipantCount data-quality check's to
        // name — this page reports volume, not integrity.
        var perMatch = (long)Math.Max(0, expectedParticipants);

        return daily
            .GroupBy(row => row.Patch, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new PatchIngestion(
                    group.Sum(row => row.Matches),
                    group.Sum(row => row.Matches) * perMatch,
                    group.Min(row => row.FirstGameStartUtc),
                    group.Max(row => row.LastGameStartUtc),
                    group.Sum(row => row.PendingTimeline),
                    group.Sum(row => row.PendingSynergy),
                    group.Sum(row => row.PendingMatchupLead),
                    group.Sum(row => row.PendingBans),
                    [.. group
                        .OrderBy(row => row.Date, StringComparer.Ordinal)
                        .Select(row => new PatchCoverageDayReadModel
                        {
                            Date = row.Date,
                            Matches = row.Matches,
                            Participants = row.Matches * perMatch
                        })]),
                StringComparer.Ordinal);
    }

    public async Task<IReadOnlyDictionary<string, PatchCoverage>> LoadCoverageAsync(
        int queueId,
        IReadOnlyList<string> coveredPatches,
        IReadOnlyDictionary<string, List<string>> scopeVersionsByPatch,
        int floor,
        PatchCoverageOptions settings,
        CancellationToken ct)
    {
        var versions = coveredPatches
            .SelectMany(patch => scopeVersionsByPatch.GetValueOrDefault(patch) ?? [])
            .ToList();

        if (versions.Count == 0)
        {
            return new Dictionary<string, PatchCoverage>(StringComparer.Ordinal);
        }

        // Exactly the grouping ChampionSummariesQueryService runs for the public
        // directory: same queue filter, same (champion, lane) key, same summed games
        // — and, since #1346, the same mains-only population. The two must agree:
        // this panel exists to tell an operator what the directory will show.
        var groups = await db.ChampionAggregateScopes
            .AsNoTracking()
            .Where(scope => scope.QueueId == queueId && versions.Contains(scope.GameVersion))
            .Where(scope => scope.IsMain)
            .GroupBy(scope => new { scope.GameVersion, scope.ChampionId, scope.Position })
            .Select(group => new
            {
                group.Key.GameVersion,
                group.Key.ChampionId,
                group.Key.Position,
                Games = group.Sum(scope => scope.Games),
                Rows = group.LongCount(),
                LastAggregatedAtUtc = group.Max(scope => scope.AggregatedAtUtc)
            })
            .ToListAsync(ct);

        var limit = Math.Max(1, settings.ThinLineLimit);

        return groups
            // Collapse the raw GameVersion forms that normalise onto the same patch before
            // anything is counted: two forms of one patch are one patch, not two.
            .GroupBy(row => PatchVersion.Normalize(row.GameVersion), StringComparer.Ordinal)
            .ToDictionary(
                patchGroup => patchGroup.Key,
                patchGroup =>
                {
                    var buildRows = patchGroup.Sum(row => row.Rows);
                    var buildChampions = patchGroup.Select(row => row.ChampionId).Distinct().Count();
                    var buildLast = patchGroup.Max(row => row.LastAggregatedAtUtc);

                    // Folded through the shared definition, not a local copy of it: the
                    // count this page reports and the count the servable bar gates
                    // serving on (#1109) have to be the same number, or the page
                    // certifies a patch the site refused — or worse, blesses one it
                    // switched onto. Lane-less rows are dropped inside Fold.
                    var lines = ChampionDirectoryLines.Fold(patchGroup.Select(row =>
                        new ChampionDirectoryLine(patchGroup.Key, row.ChampionId, row.Position, row.Games)));

                    var pastFloor = lines.Where(line => ChampionDirectoryLines.ClearsFloor(line, floor)).ToList();

                    // Primary lanes only, closest to the floor first — the shared definition
                    // says why the off-role tail is not named (#1442). The tail is still in
                    // the coverage figures: it is Lines minus LinesPastFloor minus this.
                    var below = ChampionDirectoryLines.BelowFloorOnPrimaryLane(lines, floor);

                    return new PatchCoverage(
                        lines.Count,
                        pastFloor.Count,
                        lines.Select(line => line.ChampionId).Distinct().Count(),
                        pastFloor.Select(line => line.ChampionId).Distinct().Count(),
                        below.Count,
                        [.. below
                            .Take(limit)
                            .Select(line => new PatchThinLineReadModel
                            {
                                ChampionId = line.ChampionId,
                                Position = line.Position,
                                Games = line.Games,
                                GamesToFloor = floor - line.Games
                            })],
                        buildRows,
                        buildChampions,
                        buildLast);
                },
                StringComparer.Ordinal);
    }

    private sealed record PatchDaySqlRow(
        string Patch,
        string Date,
        long Matches,
        DateTime? FirstGameStartUtc,
        DateTime? LastGameStartUtc,
        long PendingTimeline,
        long PendingSynergy,
        long PendingMatchupLead,
        long PendingBans);
}

internal sealed record PatchIngestion(
    long Matches,
    long Participants,
    DateTime? FirstGameStartUtc,
    DateTime? LastGameStartUtc,
    long PendingTimeline,
    long PendingSynergy,
    long PendingMatchupLead,
    long PendingBans,
    IReadOnlyList<PatchCoverageDayReadModel> Daily);

internal sealed record PatchCoverage(
    long Lines,
    long LinesPastFloor,
    long Champions,
    long ChampionsPastFloor,
    long BelowFloorCount,
    IReadOnlyList<PatchThinLineReadModel> BelowFloor,
    long BuildRows,
    long BuildChampions,
    DateTime? BuildLastAggregatedAtUtc);
