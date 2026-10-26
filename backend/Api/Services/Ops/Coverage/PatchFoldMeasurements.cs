using Core.Lol.Patches;
using Data;
using Microsoft.EntityFrameworkCore;

namespace TrueMain.Services.Ops.Coverage;

/// <summary>
/// The per-fold half of the patch-coverage reads (#1033): the catalogue of fold tables
/// and one grouped rollup per table. Each fold is measured in isolation, so one slow or
/// broken table yields <c>unknown</c> with the reason attached rather than blinding the
/// others.
/// </summary>
internal sealed class PatchFoldMeasurements(TrueMainDbContext db, ILogger logger)
{
    /// <summary>
    /// One grouped rollup per fold table, over every patch rather than only the covered
    /// ones — the same scan then yields both the per-patch numbers and the oldest patch
    /// the fold has ever written, which is what turns a zero into "not measured before".
    /// </summary>
    public async Task<IReadOnlyList<FoldMeasurement>> LoadAsync(
        IReadOnlyDictionary<string, PatchCoverage> coverage,
        CancellationToken ct)
    {
        var measurements = new List<FoldMeasurement>
        {
            // Builds ride the coverage rollup that has already been read: it is the same
            // table on the same filter, so re-scanning it would buy nothing.
            BuildsFold(coverage)
        };

        foreach (var fold in DerivedFolds)
        {
            measurements.Add(await MeasureFoldAsync(fold, ct));
        }

        return measurements;
    }

    private static FoldMeasurement BuildsFold(IReadOnlyDictionary<string, PatchCoverage> coverage)
    {
        var spec = new FoldSpec(
            "builds",
            "Builds — champion_aggregate_scopes",
            "The table every patch-scoped public read rests on: the directory, the tier list and the build tabs. "
                + "Replace-by-scope per account, so it carries no per-match backlog.",
            Pending: null);

        var byPatch = coverage.ToDictionary(
            entry => entry.Key,
            entry => new FoldPatchRow(entry.Value.BuildRows, entry.Value.BuildChampions, entry.Value.BuildLastAggregatedAtUtc),
            StringComparer.Ordinal);

        // No first-measured cutoff: scopes have existed for the whole corpus, so an empty
        // patch here means the fold has not run, not that the patch is out of scope.
        return new FoldMeasurement(spec, byPatch, FirstMeasuredPatch: null, UnknownReason: null);
    }

    private async Task<FoldMeasurement> MeasureFoldAsync(DerivedFold fold, CancellationToken ct)
    {
        var spec = fold.Spec;

        try
        {
            var rows = await db.Database.SqlQueryRaw<FoldPatchSqlRow>(fold.Sql).ToListAsync(ct);

            var byPatch = rows
                .Where(row => PatchVersion.TryParse(row.Patch, out _))
                .GroupBy(row => PatchVersion.Normalize(row.Patch), StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => new FoldPatchRow(
                        group.Sum(row => row.Rows),
                        group.Sum(row => row.Champions),
                        group.Max(row => row.LastAggregatedAtUtc)),
                    StringComparer.Ordinal);

            // Only patches the fold actually produced something on count as "measured":
            // a row group with zero rows is what the FILTER variants (lane outcomes,
            // per-opponent spikes) return for a patch the fold predates.
            var firstMeasured = byPatch
                .Where(entry => entry.Value.Rows > 0)
                .Select(entry => entry.Key)
                .OrderBy(PatchVersion.Parse)
                .FirstOrDefault();

            return new FoldMeasurement(spec, byPatch, firstMeasured, UnknownReason: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Same rule as the detector panel: one fold's broken or unaffordable rollup
            // must not blind the other six, and must not read as an empty fold either.
            logger.LogWarning(ex, "Patch-coverage fold {Fold} failed to measure", spec.Key);
            return new FoldMeasurement(spec, new Dictionary<string, FoldPatchRow>(StringComparer.Ordinal), null, ex.Message);
        }
        finally
        {
            ct.ThrowIfCancellationRequested();
        }
    }

    /// <summary>
    /// The folds read from their own table. Each is one grouped rollup keyed on the
    /// already-normalised <c>Patch</c> column; the lane-outcome variant reads the matchup
    /// table through a row filter rather than a table of its own.
    /// </summary>
    private static readonly DerivedFold[] DerivedFolds =
    [
        new(
            new FoldSpec(
                "matchups",
                "Matchups — champion_matchup_stats",
                "Champion vs lane opponent win rates. Additive, so a thin patch fills in as matches fold.",
                ingestion => ingestion?.PendingMatchupLead),
            PerChampionRollup("champion_matchup_stats", rowFilter: null)),
        new(
            new FoldSpec(
                "laneOutcomes",
                "Lane outcomes — champion_matchup_stats (LaneGames)",
                "The 15-minute lane verdict folded onto the matchup rows (#919), in the same pass as the counts "
                    + "above (#1445) — hence the same pending column. It still lags them, because a lane is only "
                    + "judged when both participants have a timeline snapshot.",
                ingestion => ingestion?.PendingMatchupLead),
            PerChampionRollup("champion_matchup_stats", rowFilter: "\"LaneGames\" > 0")),
        new(
            new FoldSpec(
                "bans",
                "Bans — champion_ban_stats",
                "Ban counts per patch and elo band (#920). One-shot: raw match payloads are not kept, so the matches "
                    + "that predate the fold were flagged as already folded and can never contribute.",
                ingestion => ingestion?.PendingBans),
            PerChampionRollup("champion_ban_stats", rowFilter: null)),
        new(
            new FoldSpec(
                "synergies",
                "Synergies — champion_synergy_stats",
                "Per-pairing win rates. Read behind the highest floor on the site (ChampionsList:MinSynergyGames), so "
                    + "it clears later than the directory does.",
                ingestion => ingestion?.PendingSynergy),
            PerChampionRollup("champion_synergy_stats", rowFilter: null))
    ];

    /// <summary>
    /// Rows, champions and newest fold time per patch of one fold table, counting only the
    /// rows <paramref name="rowFilter"/> admits (every row when null).
    /// </summary>
    /// <remarks>
    /// Grouped per (patch, champion) first and rolled up per patch second (#2302): the
    /// one-level form's <c>count(DISTINCT "ChampionId")</c> sorts the whole table, which is
    /// where most of each fold's time went; the two-level form hashes it and returns the
    /// same figures. A patch whose rows the filter all rejects still yields a row, with zero
    /// rows and champions — the "fold predates this patch" signal the caller reads. Both
    /// arguments are compile-time constants from <see cref="DerivedFolds"/>, never input.
    /// </remarks>
    private static string PerChampionRollup(string table, string? rowFilter)
    {
        var filter = rowFilter is null ? string.Empty : $" FILTER (WHERE {rowFilter})";
        return $"""
            SELECT
                per_champion."Patch" AS "Patch",
                sum(per_champion."Rows")::bigint AS "Rows",
                count(*) FILTER (WHERE per_champion."Rows" > 0) AS "Champions",
                max(per_champion."LastAggregatedAtUtc") AS "LastAggregatedAtUtc"
            FROM (
                SELECT
                    "Patch",
                    "ChampionId",
                    count(*){filter} AS "Rows",
                    max("AggregatedAtUtc"){filter} AS "LastAggregatedAtUtc"
                FROM {table}
                GROUP BY "Patch", "ChampionId"
            ) per_champion
            GROUP BY per_champion."Patch"
            """;
    }

    /// <summary>A fold measured by its own grouped rollup, as opposed to one riding a scan already paid for.</summary>
    private sealed record DerivedFold(FoldSpec Spec, string Sql);

    private sealed record FoldPatchSqlRow(string Patch, long Rows, long Champions, DateTime? LastAggregatedAtUtc);
}

internal sealed record FoldSpec(
    string Key,
    string Label,
    string Note,
    Func<PatchIngestion?, long?>? Pending);

internal sealed record FoldMeasurement(
    FoldSpec Spec,
    IReadOnlyDictionary<string, FoldPatchRow> ByPatch,
    string? FirstMeasuredPatch,
    string? UnknownReason);

internal sealed record FoldPatchRow(long Rows, long Champions, DateTime? LastAggregatedAtUtc);
