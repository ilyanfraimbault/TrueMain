using Core.Options;
using Data;
using Data.Ops.Mongo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Processes;

namespace TrueMain.Services.Ops.Stats;

public interface IAggregationStatsQueryService
{
    Task<AggregationsReadModel> GetAsync(CancellationToken ct);
}

/// <summary>
/// The admin Aggregation panel (#2303). Every Postgres figure is a catalog lookup or an index
/// probe, never a pass over a fold table:
/// <list type="bullet">
/// <item>row counts are the planner's estimates, as <c>/database</c> and <c>/health</c> show them
/// (an exact <c>count(*)</c> of a fold table is a full pass over it);</item>
/// <item>distinct champions are a skip scan of an index leading on the champion — about 170
/// probes instead of a <c>DISTINCT</c> over every row;</item>
/// <item>the two backlogs are counts over partial indexes that hold only the pending rows.</item>
/// </list>
/// Distinct patches and the newest row timestamp per family were dropped rather than paid
/// for: no fold table is indexed on either column, <c>/patch-coverage</c> measures each fold
/// per patch, and the producing process's last success says when the family last wrote.
/// </summary>
public sealed class AggregationStatsQueryService(
    TrueMainDbContext db,
    IProcessRunStore processRunStore,
    IOptions<MainAnalysisOptions> mainAnalysisOptions,
    TimeProvider timeProvider) : IAggregationStatsQueryService
{
    private static readonly string[] ProcessNames =
    [
        "ChampionPatternAggregation",
        "ChampionMatchupLeadAggregation",
        "ChampionSynergyAggregation",
        "MainAnalysis",
        "MatchParticipantEloBracketEnrichment"
    ];

    private static readonly string[] Tables =
    [
        "champion_aggregate_scopes",
        "champion_aggregate_patterns",
        "champion_matchup_stats",
        "champion_synergy_stats",
        "champion_synergy_baseline_stats",
        "main_champion_stats"
    ];

    public async Task<AggregationsReadModel> GetAsync(CancellationToken ct)
    {
        var queueId = (int)mainAnalysisOptions.Value.QueueId;

        // The two Mongo reads run alongside the Postgres ones: different stores, and the
        // DbContext is only ever used by one of the three tasks.
        var latestRunsTask = processRunStore.GetLatestPerProcessAsync(ProcessNames, onlySuccesses: false, ct);
        var latestSuccessesTask = processRunStore.GetLatestPerProcessAsync(ProcessNames, onlySuccesses: true, ct);

        var estimates = await LoadRowEstimatesAsync(ct);
        var coverage = await LoadCoverageAsync(queueId, ct);
        var latestRuns = await latestRunsTask;
        var latestSuccesses = await latestSuccessesTask;

        AggregationRunReadModel? Run(string processName) => BuildRunReadModel(
            latestRuns.FirstOrDefault(run => run.ProcessName == processName),
            latestSuccesses.FirstOrDefault(run => run.ProcessName == processName));

        return new AggregationsReadModel
        {
            QueueId = queueId,
            MeasuredAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            Families =
            [
                Family("builds", "ChampionPatternAggregation", coverage.BuildChampions, estimates, Run,
                    "champion_aggregate_scopes", "champion_aggregate_patterns"),
                Family("matchups", "ChampionMatchupLeadAggregation", coverage.MatchupChampions, estimates, Run,
                    "champion_matchup_stats"),
                Family("synergies", "ChampionSynergyAggregation", coverage.SynergyChampions, estimates, Run,
                    "champion_synergy_stats", "champion_synergy_baseline_stats"),
                Family("mains", "MainAnalysis", coverage.MainChampions, estimates, Run,
                    "main_champion_stats")
            ],
            Backlog = new AggregationBacklogReadModel
            {
                PendingSynergyMatches = coverage.PendingSynergyMatches,
                PendingEloBracketParticipants = coverage.PendingEloBracketParticipants
            }
        };
    }

    private static AggregationFamilyReadModel Family(
        string key,
        string processName,
        long distinctChampions,
        IReadOnlyDictionary<string, long> estimates,
        Func<string, AggregationRunReadModel?> run,
        params string[] tables)
    {
        var counts = tables
            .Select(table => new AggregationTableCountReadModel
            {
                Table = table,
                RowEstimate = estimates.GetValueOrDefault(table)
            })
            .ToList();

        return new AggregationFamilyReadModel
        {
            Key = key,
            ProcessName = processName,
            Tables = counts,
            TotalRowEstimate = counts.Sum(count => count.RowEstimate),
            DistinctChampions = (int)distinctChampions,
            LastRun = run(processName)
        };
    }

    /// <summary>
    /// The fold tables' live-row estimates. <c>n_live_tup</c> first, the figure <c>/database</c>
    /// prints, so the two pages agree; <c>reltuples</c> when the statistics view holds no count
    /// for a table that has rows (its counters reset with the statistics, while
    /// <c>reltuples</c> is what the last VACUUM or ANALYZE measured).
    /// </summary>
    private async Task<IReadOnlyDictionary<string, long>> LoadRowEstimatesAsync(CancellationToken ct)
    {
        var rows = await db.Database
            .SqlQuery<TableRowEstimate>($"""
                SELECT s.relname AS "TableName",
                       CASE WHEN s.n_live_tup > 0 THEN s.n_live_tup
                            ELSE GREATEST(c.reltuples, 0)::bigint END AS "RowEstimate"
                FROM pg_catalog.pg_stat_user_tables s
                JOIN pg_catalog.pg_class c ON c.oid = s.relid
                WHERE s.schemaname = 'public' AND s.relname = ANY ({Tables})
                """)
            .ToListAsync(ct);

        return rows.ToDictionary(row => row.TableName, row => row.RowEstimate, StringComparer.Ordinal);
    }

    /// <summary>
    /// Champion coverage and the backlogs, in one round trip. Each champion count walks an
    /// index that leads on <c>ChampionId</c> one distinct value at a time (a recursive
    /// "loose index scan"); mains walk the active-main partial index's
    /// <c>(PlatformId, ChampionId)</c> pairs the same way. The backlog predicates match the
    /// partial indexes <c>IX_matches_synergy_pending</c> and
    /// <c>IX_match_participants_elo_bracket_pending</c> word for word, so each count reads only
    /// the pending rows.
    /// </summary>
    private async Task<CoverageRow> LoadCoverageAsync(int queueId, CancellationToken ct)
        => await db.Database
            .SqlQuery<CoverageRow>($"""
                SELECT
                  (WITH RECURSIVE ids(id) AS (
                     SELECT min("ChampionId") FROM champion_aggregate_scopes
                     UNION ALL
                     SELECT (SELECT min(s."ChampionId") FROM champion_aggregate_scopes s WHERE s."ChampionId" > ids.id)
                     FROM ids WHERE ids.id IS NOT NULL)
                   SELECT count(id) FROM ids) AS "BuildChampions",
                  (WITH RECURSIVE ids(id) AS (
                     SELECT min("ChampionId") FROM champion_matchup_stats
                     UNION ALL
                     SELECT (SELECT min(s."ChampionId") FROM champion_matchup_stats s WHERE s."ChampionId" > ids.id)
                     FROM ids WHERE ids.id IS NOT NULL)
                   SELECT count(id) FROM ids) AS "MatchupChampions",
                  (WITH RECURSIVE ids(id) AS (
                     SELECT min("ChampionId") FROM champion_synergy_stats
                     UNION ALL
                     SELECT (SELECT min(s."ChampionId") FROM champion_synergy_stats s WHERE s."ChampionId" > ids.id)
                     FROM ids WHERE ids.id IS NOT NULL)
                   SELECT count(id) FROM ids) AS "SynergyChampions",
                  (WITH RECURSIVE pairs(platform, champion) AS (
                     (SELECT "PlatformId", "ChampionId" FROM main_champion_stats
                      WHERE "IsMain" AND "IsActive"
                      ORDER BY "PlatformId", "ChampionId" LIMIT 1)
                     UNION ALL
                     SELECT next."PlatformId", next."ChampionId" FROM pairs, LATERAL (
                       SELECT s."PlatformId", s."ChampionId" FROM main_champion_stats s
                       WHERE s."IsMain" AND s."IsActive"
                         AND (s."PlatformId", s."ChampionId") > (pairs.platform, pairs.champion)
                       ORDER BY s."PlatformId", s."ChampionId" LIMIT 1) next)
                   SELECT count(DISTINCT champion) FROM pairs) AS "MainChampions",
                  (SELECT count(*) FROM matches
                   WHERE "QueueId" = {queueId} AND "SynergyAggregated" = false) AS "PendingSynergyMatches",
                  (SELECT count(*) FROM match_participants
                   WHERE "RiotAccountId" IS NOT NULL AND elo_bracket = '') AS "PendingEloBracketParticipants"
                """)
            .SingleAsync(ct);

    private static AggregationRunReadModel? BuildRunReadModel(
        ProcessRunDocument? latest,
        ProcessRunDocument? latestSuccess)
    {
        if (latest is null)
        {
            return null;
        }

        return new AggregationRunReadModel
        {
            Status = latest.Status.ToString().ToLowerInvariant(),
            LastStartedAtUtc = latest.StartedAtUtc,
            LastFinishedAtUtc = latest.FinishedAtUtc,
            LastSuccessAtUtc = latestSuccess?.FinishedAtUtc,
            DurationMs = latest.DurationMs,
            LastSuccessSummary = ProcessRunSummaryParsing.Parse(latestSuccess?.SummaryJson)
        };
    }

    internal sealed record TableRowEstimate(string TableName, long RowEstimate);

    internal sealed record CoverageRow(
        long BuildChampions,
        long MatchupChampions,
        long SynergyChampions,
        long MainChampions,
        long PendingSynergyMatches,
        long PendingEloBracketParticipants);
}
