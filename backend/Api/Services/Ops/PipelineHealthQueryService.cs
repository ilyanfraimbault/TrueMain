using Core.Lol.Patches;
using Core.Options;
using Data;
using Data.Entities;
using Data.Ops.Mongo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// The operator cockpit's one call (#1031). Measures what only it measures — raw-data
/// freshness per platform and the two pipeline gaps — and <em>composes</em> the signals that
/// already have panels of their own by calling those panels' services rather than
/// re-querying their tables.
///
/// <para>
/// Composing rather than re-measuring is the whole design. A cockpit that re-implemented the
/// ingestion-lag thresholds would eventually disagree with <c>/data-quality</c>, and the tile
/// that links there would be lying. The cost of the composition is the cost of the pages it
/// replaces — an operator answering "is the pipeline healthy?" opens all four today.
/// </para>
/// </summary>
public sealed class PipelineHealthQueryService(
    TrueMainDbContext db,
    IProcessRunStore processRunStore,
    IDataQualityDetectorsQueryService dataQualityDetectors,
    IDbStorageHistoryQueryService storageHistory,
    IOptions<MainAnalysisOptions> mainAnalysisOptions,
    IOptions<PipelineHealthOptions> pipelineHealthOptions,
    IOptions<StorageHistoryOptions> storageHistoryOptions,
    TimeProvider timeProvider,
    IHostEnvironment environment,
    ILogger<PipelineHealthQueryService> logger) : IPipelineHealthQueryService
{
    private static readonly string[] ProcessNames =
    [
        "Discovery",
        "Scoring",
        "MatchIngestion",
        "MainAnalysis",
        "MatchParticipantEloBracketEnrichment",
        "ChampionPatternAggregation",
        "ChampionMatchupLeadAggregation",
        "ChampionPowerspikeAggregation",
        "AccountRefresh",
        "MatchDataRetention"
    ];

    public async Task<PipelineHealthReadModel> GetAsync(CancellationToken ct)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var queueId = (int)mainAnalysisOptions.Value.QueueId;

        var processes = await BuildProcessesAsync(nowUtc, ct);
        var rawData = await BuildRawDataAsync(queueId, ct);
        var gaps = await BuildGapsAsync(rawData, ct);

        // Each composed signal is wrapped: one broken sub-signal degrades to its own
        // `unknown` with the reason on the tile, and must neither blind the others nor fail
        // the page. Same policy the detectors panel applies to a single broken detector.
        //
        // The detectors are fetched once and feed two tiles. Running them twice would double
        // the most expensive part of this call — five detectors' worth of grouped scans — to
        // produce two views of the same measurement.
        var detectorSignals = await SafeDetectorSignalsAsync(ct);

        var signals = PipelineHealthEvaluator.OrderBySeverity(
        [
            PipelineHealthEvaluator.EvaluateProcesses(processes),
            ..detectorSignals,
            await SafeSignalAsync(
                "diskForecast",
                "Disk forecast",
                "/database",
                async () => PipelineHealthEvaluator.EvaluateDiskForecast(
                    await storageHistory.GetAsync(null, ct),
                    storageHistoryOptions.Value.DiskCapacityBytes,
                    nowUtc,
                    pipelineHealthOptions.Value),
                ct)
        ]);

        var (status, headline) = PipelineHealthEvaluator.Rollup(signals);

        return new PipelineHealthReadModel
        {
            Status = status,
            Headline = headline,
            EvaluatedAtUtc = nowUtc,
            Signals = signals,
            Processes = processes,
            RawData = rawData,
            Gaps = gaps
        };
    }

    private async Task<IReadOnlyList<ProcessHealthReadModel>> BuildProcessesAsync(
        DateTime nowUtc,
        CancellationToken ct)
    {
        // The latest run per process in a single grouped pass — the Mongo shape of
        // the DISTINCT ON the Postgres implementation used (process runs moved to
        // the Mongo observability store with the rest of the admin-portal data).
        var latestRuns = await processRunStore.GetLatestPerProcessAsync(
            ProcessNames, onlySuccesses: false, ct);

        // The all-time rollup carries the one field the latest run cannot: when this
        // process last actually succeeded. Unbounded window on purpose — "last succeeded"
        // has no useful window, and a process that last worked five months ago must not
        // report the same null as one that has never worked at all.
        var rollups = await processRunStore.GetRollupsAsync(processName: null, windowStart: null, ct);

        var sanitizeErrors = environment.IsProduction();
        var processes = new List<ProcessHealthReadModel>(ProcessNames.Length);

        foreach (var processName in ProcessNames)
        {
            var run = latestRuns.FirstOrDefault(candidate => candidate.ProcessName == processName);
            if (run is null)
            {
                processes.Add(new ProcessHealthReadModel
                {
                    ProcessName = processName,
                    Status = PipelineHealthEvaluator.MissingStatus
                });
                continue;
            }

            var rollup = rollups.FirstOrDefault(candidate => candidate.ProcessName == processName);
            var effectiveStatus = ProcessRunStaleness.EffectiveStatus(
                run.Status, run.LastHeartbeatAtUtc, nowUtc);

            // Only ask for the streak when the latest run did not succeed. On a healthy
            // pipeline that is zero extra queries; on a broken one it is one small counted
            // query for the process that is actually broken.
            var consecutiveFailures = effectiveStatus == ProcessRunStatus.Success
                ? 0
                : await processRunStore.CountTerminalRunsSinceAsync(
                    processName, rollup?.LastSuccessAtUtc, ct);

            processes.Add(new ProcessHealthReadModel
            {
                ProcessName = run.ProcessName,
                Status = effectiveStatus.ToString(),
                LastStartedAtUtc = run.StartedAtUtc,
                LastFinishedAtUtc = run.FinishedAtUtc,
                LastSuccessAtUtc = rollup?.LastSuccessAtUtc,
                ConsecutiveFailures = (int)Math.Min(consecutiveFailures, int.MaxValue),
                DurationMs = run.DurationMs,
                Error = SanitizeError(run.Error, sanitizeErrors)
            });
        }

        return processes;
    }

    private async Task<RawDataFreshnessReadModel> BuildRawDataAsync(int queueId, CancellationToken ct)
    {
        var queueScopedMatches = db.Matches
            .AsNoTracking()
            .Where(match => match.QueueId == queueId);

        // Compute the latest GameStartTimeUtc per platform with a single
        // GROUP BY aggregate, then join it back to the matches set. This
        // replaces a per-row correlated subquery (which can degrade to a
        // scan-per-row or client evaluation) with one grouped scan plus a
        // hash/merge join. Ties on the max timestamp keep every matching
        // row; the downstream GroupBy resolves them deterministically.
        var latestStartByPlatform = queueScopedMatches
            .GroupBy(match => match.PlatformId)
            .Select(group => new
            {
                PlatformId = group.Key,
                LatestStart = group.Max(match => match.GameStartTimeUtc)
            });

        var latestMatchesByPlatform = await queueScopedMatches
            .Join(
                latestStartByPlatform,
                match => new { match.PlatformId, Start = match.GameStartTimeUtc },
                latest => new { latest.PlatformId, Start = latest.LatestStart },
                (match, _) => new
                {
                    match.Id,
                    match.PlatformId,
                    match.GameStartTimeUtc,
                    match.GameVersion
                })
            .OrderBy(match => match.PlatformId)
            .ThenByDescending(match => match.GameStartTimeUtc)
            .ThenByDescending(match => match.Id)
            .ToListAsync(ct);

        var platformFreshness = latestMatchesByPlatform
            .GroupBy(match => match.PlatformId)
            .Select(group =>
            {
                var latestMatch = group.First();

                return new PlatformRawDataFreshnessReadModel
                {
                    PlatformId = group.Key,
                    LatestMatchStartAtUtc = latestMatch.GameStartTimeUtc,
                    LatestPatchVersion = PatchVersion.Normalize(latestMatch.GameVersion)
                };
            })
            .ToList();

        var rawMatchCount = await queueScopedMatches.CountAsync(ct);
        var rawParticipantCount = await db.MatchParticipants
            .AsNoTracking()
            .Join(
                queueScopedMatches,
                participant => participant.MatchId,
                match => match.Id,
                (participant, _) => participant.Id)
            .CountAsync(ct);

        return new RawDataFreshnessReadModel
        {
            QueueId = queueId,
            RawMatchCount = rawMatchCount,
            RawParticipantCount = rawParticipantCount,
            Platforms = platformFreshness
        };
    }

    private async Task<PipelineGapReadModel> BuildGapsAsync(
        RawDataFreshnessReadModel rawData,
        CancellationToken ct)
    {
        // Stays nullable all the way through. Selecting the value out of the nullable and
        // taking FirstOrDefault() used to collapse "no scoped match at all" to 0001-01-01,
        // which the subtraction below then reported as a lag of about a billion minutes.
        var latestScopedRawMatchStartAtUtc = rawData.Platforms
            .Select(platform => platform.LatestMatchStartAtUtc)
            .Where(timestamp => timestamp.HasValue)
            .OrderByDescending(timestamp => timestamp!.Value)
            .FirstOrDefault();

        var latestChampionDataSignal = await db.MainChampionStats
            .AsNoTracking()
            .Select(stat => (DateTime?)stat.CalculatedAtUtc)
            .MaxAsync(ct);

        // The newest *successful* finish of each side, not the newest run: a failed
        // MatchIngestion says nothing about how far MainAnalysis trails the data it has.
        var successes = await processRunStore.GetLatestPerProcessAsync(
            ["MatchIngestion", "MainAnalysis"], onlySuccesses: true, ct);

        var latestMatchIngestionSuccess = successes
            .Where(run => run.ProcessName == "MatchIngestion")
            .Select(run => (DateTime?)run.FinishedAtUtc)
            .FirstOrDefault();

        var latestMainAnalysisSuccess = successes
            .Where(run => run.ProcessName == "MainAnalysis")
            .Select(run => (DateTime?)run.FinishedAtUtc)
            .FirstOrDefault();

        return new PipelineGapReadModel
        {
            MatchIngestionToMainAnalysisMinutes = ComputeGapMinutes(latestMatchIngestionSuccess, latestMainAnalysisSuccess),
            ChampionDataLagMinutes = ComputeGapMinutes(latestChampionDataSignal, latestScopedRawMatchStartAtUtc)
        };
    }

    /// <summary>
    /// The two tiles that read off the data-quality detectors, from one run of them. A
    /// failure here costs both tiles their measurement — they share the one query — so both
    /// report unknown with the same reason rather than one of them guessing.
    /// </summary>
    private async Task<IReadOnlyList<PipelineHealthSignalReadModel>> SafeDetectorSignalsAsync(
        CancellationToken ct)
    {
        try
        {
            var detectors = await dataQualityDetectors.GetDetectorsAsync(ct);

            return
            [
                PipelineHealthEvaluator.EvaluateDataQuality(detectors),
                PipelineHealthEvaluator.EvaluateDetectorAsSignal(
                    detectors,
                    detectorKey: "ingestionLag",
                    signalKey: "ingestionLag",
                    title: "Ingestion lag & queues")
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Pipeline-health data-quality signals failed to measure");

            var reason = SanitizeError(ex.Message, environment.IsProduction()) ?? "internal error";

            return
            [
                UnmeasurableSignal("dataQuality", "Data quality", "/data-quality", reason),
                UnmeasurableSignal("ingestionLag", "Ingestion lag & queues", "/data-quality", reason)
            ];
        }
        finally
        {
            ct.ThrowIfCancellationRequested();
        }
    }

    private async Task<PipelineHealthSignalReadModel> SafeSignalAsync(
        string key,
        string title,
        string detailPath,
        Func<Task<PipelineHealthSignalReadModel>> build,
        CancellationToken ct)
    {
        try
        {
            return await build();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Pipeline-health signal {Signal} failed to measure", key);

            return UnmeasurableSignal(
                key, title, detailPath, SanitizeError(ex.Message, environment.IsProduction()) ?? "internal error");
        }
        finally
        {
            ct.ThrowIfCancellationRequested();
        }
    }

    private static PipelineHealthSignalReadModel UnmeasurableSignal(
        string key,
        string title,
        string detailPath,
        string reason)
        => new()
        {
            Key = key,
            Title = title,
            Status = "unknown",
            Headline = "This signal could not be measured.",
            UnknownReason = reason,
            DetailPath = detailPath
        };

    private static double? ComputeGapMinutes(DateTime? from, DateTime? to)
    {
        if (from is null || to is null)
        {
            return null;
        }

        return (to.Value - from.Value).TotalMinutes;
    }

    private static string? SanitizeError(string? error, bool sanitize)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return null;
        }

        if (!sanitize)
        {
            // Dev/QA: surface the full payload (stack, paths, message) so
            // operators can diagnose without poking at logs.
            return error;
        }

        // Production: never echo raw exception text to API clients. It can
        // leak filesystem paths, connection-string fragments or internal
        // type names. The status field already carries the failure signal;
        // operators reach for logs/tracing for the real cause.
        return "internal error";
    }
}
