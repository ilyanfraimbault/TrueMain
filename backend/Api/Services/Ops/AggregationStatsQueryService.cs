using Core.Lol.Patches;
using Core.Options;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public sealed class AggregationStatsQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> mainAnalysisOptions) : IAggregationStatsQueryService
{
    private static readonly string[] ProcessNames =
    [
        "ChampionPatternAggregation",
        "ChampionMatchupLeadAggregation",
        "ChampionPowerspikeAggregation",
        "MainAnalysis",
        "MatchParticipantEloBracketEnrichment"
    ];

    public async Task<AggregationsReadModel> GetAsync(CancellationToken ct)
    {
        var queueId = (int)mainAnalysisOptions.Value.QueueId;

        var latestRuns = await LoadLatestRunsAsync(onlySuccesses: false, ct);
        var latestSuccesses = await LoadLatestRunsAsync(onlySuccesses: true, ct);

        var runsByProcess = ProcessNames.ToDictionary(
            processName => processName,
            processName => BuildRunReadModel(
                latestRuns.FirstOrDefault(run => run.ProcessName == processName),
                latestSuccesses.FirstOrDefault(run => run.ProcessName == processName)));

        var families = new List<AggregationFamilyReadModel>
        {
            await BuildBuildsFamilyAsync(runsByProcess, ct),
            await BuildMatchupsFamilyAsync(runsByProcess, ct),
            await BuildTimelineLeadsFamilyAsync(runsByProcess, ct),
            await BuildPowerspikesFamilyAsync(runsByProcess, ct),
            await BuildMainsFamilyAsync(runsByProcess, ct)
        };

        var timelineIngestedMatches = await db.Matches
            .AsNoTracking()
            .LongCountAsync(match => match.QueueId == queueId && match.TimelineIngested, ct);
        var pendingPowerspikeMatches = await db.Matches
            .AsNoTracking()
            .LongCountAsync(
                match => match.QueueId == queueId && match.TimelineIngested && !match.PowerspikeAggregated,
                ct);
        var pendingEloBracketParticipants = await db.MatchParticipants
            .AsNoTracking()
            .LongCountAsync(
                participant => participant.RiotAccountId != null && participant.EloBracket == string.Empty,
                ct);

        return new AggregationsReadModel
        {
            QueueId = queueId,
            Families = families,
            Backlog = new AggregationBacklogReadModel
            {
                PendingPowerspikeMatches = pendingPowerspikeMatches,
                PendingEloBracketParticipants = pendingEloBracketParticipants,
                TimelineIngestedMatches = timelineIngestedMatches
            }
        };
    }

    private async Task<AggregationFamilyReadModel> BuildBuildsFamilyAsync(
        IReadOnlyDictionary<string, AggregationRunReadModel?> runsByProcess,
        CancellationToken ct)
    {
        var scopes = db.ChampionAggregateScopes.AsNoTracking();

        var scopeRows = await scopes.LongCountAsync(ct);
        var patternRows = await db.ChampionAggregatePatterns.AsNoTracking().LongCountAsync(ct);
        var distinctChampions = await scopes.Select(scope => scope.ChampionId).Distinct().CountAsync(ct);

        // Scopes store the raw game version; normalize client-side so patches
        // count as "MAJOR.MINOR" like the other families.
        var gameVersions = await scopes.Select(scope => scope.GameVersion).Distinct().ToListAsync(ct);
        var distinctPatches = gameVersions
            .Select(PatchVersion.Normalize)
            .Distinct()
            .Count();

        var lastAggregatedAtUtc = await scopes.MaxAsync(scope => (DateTime?)scope.AggregatedAtUtc, ct);

        return new AggregationFamilyReadModel
        {
            Key = "builds",
            ProcessName = "ChampionPatternAggregation",
            Tables =
            [
                new AggregationTableCountReadModel { Table = "champion_aggregate_scopes", Rows = scopeRows },
                new AggregationTableCountReadModel { Table = "champion_aggregate_patterns", Rows = patternRows }
            ],
            TotalRows = scopeRows + patternRows,
            DistinctChampions = distinctChampions,
            DistinctPatches = distinctPatches,
            LastAggregatedAtUtc = lastAggregatedAtUtc,
            LastRun = runsByProcess["ChampionPatternAggregation"]
        };
    }

    private async Task<AggregationFamilyReadModel> BuildMatchupsFamilyAsync(
        IReadOnlyDictionary<string, AggregationRunReadModel?> runsByProcess,
        CancellationToken ct)
    {
        var stats = db.ChampionMatchupStats.AsNoTracking();

        var rows = await stats.LongCountAsync(ct);
        var distinctChampions = await stats.Select(stat => stat.ChampionId).Distinct().CountAsync(ct);
        var distinctPatches = await stats.Select(stat => stat.Patch).Distinct().CountAsync(ct);
        var lastAggregatedAtUtc = await stats.MaxAsync(stat => (DateTime?)stat.AggregatedAtUtc, ct);

        return new AggregationFamilyReadModel
        {
            Key = "matchups",
            ProcessName = "ChampionMatchupLeadAggregation",
            Tables = [new AggregationTableCountReadModel { Table = "champion_matchup_stats", Rows = rows }],
            TotalRows = rows,
            DistinctChampions = distinctChampions,
            DistinctPatches = distinctPatches,
            LastAggregatedAtUtc = lastAggregatedAtUtc,
            LastRun = runsByProcess["ChampionMatchupLeadAggregation"]
        };
    }

    private async Task<AggregationFamilyReadModel> BuildTimelineLeadsFamilyAsync(
        IReadOnlyDictionary<string, AggregationRunReadModel?> runsByProcess,
        CancellationToken ct)
    {
        var stats = db.ChampionTimelineLeadStats.AsNoTracking();

        var rows = await stats.LongCountAsync(ct);
        var distinctChampions = await stats.Select(stat => stat.ChampionId).Distinct().CountAsync(ct);
        var distinctPatches = await stats.Select(stat => stat.Patch).Distinct().CountAsync(ct);
        var lastAggregatedAtUtc = await stats.MaxAsync(stat => (DateTime?)stat.AggregatedAtUtc, ct);

        return new AggregationFamilyReadModel
        {
            Key = "timelineLeads",
            ProcessName = "ChampionMatchupLeadAggregation",
            Tables = [new AggregationTableCountReadModel { Table = "champion_timeline_lead_stats", Rows = rows }],
            TotalRows = rows,
            DistinctChampions = distinctChampions,
            DistinctPatches = distinctPatches,
            LastAggregatedAtUtc = lastAggregatedAtUtc,
            LastRun = runsByProcess["ChampionMatchupLeadAggregation"]
        };
    }

    private async Task<AggregationFamilyReadModel> BuildPowerspikesFamilyAsync(
        IReadOnlyDictionary<string, AggregationRunReadModel?> runsByProcess,
        CancellationToken ct)
    {
        var curves = db.ChampionPowerspikeCurveStats.AsNoTracking();

        var curveRows = await curves.LongCountAsync(ct);
        var eventRows = await db.ChampionPowerspikeEventStats.AsNoTracking().LongCountAsync(ct);
        var sigmaRows = await db.PowerspikeSigmaStats.AsNoTracking().LongCountAsync(ct);
        var distinctChampions = await curves.Select(stat => stat.ChampionId).Distinct().CountAsync(ct);
        var distinctPatches = await curves.Select(stat => stat.Patch).Distinct().CountAsync(ct);
        var lastAggregatedAtUtc = await curves.MaxAsync(stat => (DateTime?)stat.AggregatedAtUtc, ct);

        return new AggregationFamilyReadModel
        {
            Key = "powerspikes",
            ProcessName = "ChampionPowerspikeAggregation",
            Tables =
            [
                new AggregationTableCountReadModel { Table = "champion_powerspike_curve_stats", Rows = curveRows },
                new AggregationTableCountReadModel { Table = "champion_powerspike_event_stats", Rows = eventRows },
                new AggregationTableCountReadModel { Table = "powerspike_sigma_stats", Rows = sigmaRows }
            ],
            TotalRows = curveRows + eventRows + sigmaRows,
            DistinctChampions = distinctChampions,
            DistinctPatches = distinctPatches,
            LastAggregatedAtUtc = lastAggregatedAtUtc,
            LastRun = runsByProcess["ChampionPowerspikeAggregation"]
        };
    }

    private async Task<AggregationFamilyReadModel> BuildMainsFamilyAsync(
        IReadOnlyDictionary<string, AggregationRunReadModel?> runsByProcess,
        CancellationToken ct)
    {
        var stats = db.MainChampionStats.AsNoTracking();

        var rows = await stats.LongCountAsync(ct);
        var distinctChampions = await stats.Select(stat => stat.ChampionId).Distinct().CountAsync(ct);
        var lastCalculatedAtUtc = await stats.MaxAsync(stat => (DateTime?)stat.CalculatedAtUtc, ct);

        return new AggregationFamilyReadModel
        {
            Key = "mains",
            ProcessName = "MainAnalysis",
            Tables = [new AggregationTableCountReadModel { Table = "main_champion_stats", Rows = rows }],
            TotalRows = rows,
            DistinctChampions = distinctChampions,
            // Mains are per-account aggregates over the full corpus — no patch axis.
            DistinctPatches = null,
            LastAggregatedAtUtc = lastCalculatedAtUtc,
            LastRun = runsByProcess["MainAnalysis"]
        };
    }

    private async Task<List<ProcessRun>> LoadLatestRunsAsync(bool onlySuccesses, CancellationToken ct)
    {
        // Postgres DISTINCT ON gives the newest row per process in one pass
        // (same rationale as PipelineHealthQueryService). Two variants: the
        // latest run regardless of outcome, and the latest success — they
        // diverge exactly when the most recent run failed or was abandoned.
        var successStatus = (int)ProcessRunStatus.Success;

        return onlySuccesses
            ? await db.ProcessRuns
                .FromSqlInterpolated(
                    $"""
                     SELECT DISTINCT ON ("ProcessName") *
                     FROM process_runs
                     WHERE "ProcessName" = ANY({ProcessNames}) AND "Status" = {successStatus}
                     ORDER BY "ProcessName", "FinishedAtUtc" DESC
                     """)
                .AsNoTracking()
                .ToListAsync(ct)
            : await db.ProcessRuns
                .FromSqlInterpolated(
                    $"""
                     SELECT DISTINCT ON ("ProcessName") *
                     FROM process_runs
                     WHERE "ProcessName" = ANY({ProcessNames})
                     ORDER BY "ProcessName", "FinishedAtUtc" DESC
                     """)
                .AsNoTracking()
                .ToListAsync(ct);
    }

    private static AggregationRunReadModel? BuildRunReadModel(ProcessRun? latest, ProcessRun? latestSuccess)
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
            LastSuccessSummary = latestSuccess?.Summary?.RootElement.Clone()
        };
    }
}
