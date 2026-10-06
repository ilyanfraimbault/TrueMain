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
    public async Task<IReadOnlyDictionary<string, PatchIngestion>> LoadIngestionAsync(
        int queueId,
        string[] patches,
        CancellationToken ct)
    {
        // Normalising in SQL rather than reading every row: split_part matches
        // PatchVersion.Normalize for anything with two or more segments, and the
        // `= ANY(patches)` filter only ever admits values that already parsed, so a
        // degenerate GameVersion is excluded from both sides consistently.
        FormattableString dailySql = $"""
            SELECT
                split_part(m."GameVersion", '.', 1) || '.' || split_part(m."GameVersion", '.', 2) AS "Patch",
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
              AND split_part(m."GameVersion", '.', 1) || '.' || split_part(m."GameVersion", '.', 2) = ANY({patches})
            GROUP BY 1, 2
            """;

        var daily = await db.Database.SqlQuery<PatchDaySqlRow>(dailySql).ToListAsync(ct);

        // Participants ride a second statement rather than a join in the one above: a
        // join multiplies the match rows ten-fold, and `count(DISTINCT m."Id")` over that
        // product costs far more than scanning `matches` twice.
        FormattableString participantsSql = $"""
            SELECT
                split_part(m."GameVersion", '.', 1) || '.' || split_part(m."GameVersion", '.', 2) AS "Patch",
                to_char((m."GameStartTimeUtc" AT TIME ZONE 'UTC')::date, 'YYYY-MM-DD') AS "Date",
                count(*) AS "Participants"
            FROM matches m
            JOIN match_participants p ON p."MatchId" = m."Id"
            WHERE m."QueueId" = {queueId}
              AND split_part(m."GameVersion", '.', 1) || '.' || split_part(m."GameVersion", '.', 2) = ANY({patches})
            GROUP BY 1, 2
            """;

        var participants = (await db.Database.SqlQuery<PatchDayParticipantsSqlRow>(participantsSql).ToListAsync(ct))
            .ToDictionary(row => (row.Patch, row.Date), row => row.Participants);

        return daily
            .GroupBy(row => row.Patch, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new PatchIngestion(
                    group.Sum(row => row.Matches),
                    group.Sum(row => participants.GetValueOrDefault((row.Patch, row.Date))),
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
                            Participants = participants.GetValueOrDefault((row.Patch, row.Date))
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

    private sealed record PatchDayParticipantsSqlRow(string Patch, string Date, long Participants);
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
