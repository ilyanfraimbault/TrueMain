using Core.Lol.Patches;
using Core.Options;
using Data;
using Data.Entities;
using Data.Ops.Mongo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Database;
using TrueMain.Services.Ops.DataQuality;
using TrueMain.Services.Ops.Processes;

namespace TrueMain.Services.Ops.Health;

public interface IPipelineHealthQueryService
{
    Task<PipelineHealthReadModel> GetAsync(CancellationToken ct);
}

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
    IRegionBalanceQueryService regionBalance,
    IOptions<MainAnalysisOptions> mainAnalysisOptions,
    IOptions<PipelineHealthOptions> pipelineHealthOptions,
    IOptions<StorageHistoryOptions> storageHistoryOptions,
    TimeProvider timeProvider,
    IHostEnvironment environment,
    IMemoryCache cache,
    ILogger<PipelineHealthQueryService> logger) : IPipelineHealthQueryService
{
    /// <summary>
    /// The raw-corpus totals are the most expensive read of the whole evaluation (#1427): the
    /// participant count walks every participant row of the queue, which took tens of seconds
    /// on a busy host — more than every detector together. They are informational (no signal
    /// is judged from them) and grow on an ingestion cadence, so they are measured on their
    /// own, longer clock than the 30-second payload; same bound as the operator champion stats,
    /// which scan the same join.
    /// </summary>
    internal static readonly TimeSpan RawCorpusCountsTtl = TimeSpan.FromMinutes(10);

    private static readonly string[] ProcessNames =
    [
        "Discovery",
        "Scoring",
        "MatchIngestion",
        "MainAnalysis",
        "MatchParticipantEloBracketEnrichment",
        "ChampionPatternAggregation",
        "ChampionMatchupLeadAggregation",
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
        var balance = await SafeRegionBalanceAsync(nowUtc, ct);

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
            Gaps = gaps,
            RegionBalance = balance
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
            // query for the process that is actually broken. Skipped counts as healthy
            // here (#1149): a cadence guard declining to run is the guard working, not a
            // failure, so it must not open a streak on an otherwise-fine process. Cancelled
            // is healthy for the neighbouring reason (#1513): the run stopped because the
            // host asked it to, which is what a redeploy looks like, not a defect.
            var consecutiveFailures = effectiveStatus is ProcessRunStatus.Success
                or ProcessRunStatus.Skipped
                or ProcessRunStatus.Cancelled
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

        var counts = await GetRawCorpusCountsAsync(queueId, queueScopedMatches, ct);

        return new RawDataFreshnessReadModel
        {
            QueueId = queueId,
            RawMatchCount = counts.Matches,
            RawParticipantCount = counts.Participants,
            Platforms = platformFreshness
        };
    }

    private async Task<RawCorpusCounts> GetRawCorpusCountsAsync(
        int queueId,
        IQueryable<Match> queueScopedMatches,
        CancellationToken ct)
    {
        // Keyed by queue: the queue is configuration, and a count of one queue's corpus must
        // never be read back as another's. No coalescer of its own — the only caller is the
        // payload pass, which CachedPipelineHealthQueryService already single-flights.
        var cacheKey = $"ops:pipeline-health:raw-corpus:{queueId}";
        if (cache.TryGetValue(cacheKey, out RawCorpusCounts? cached) && cached is not null)
        {
            return cached;
        }

        var matches = await queueScopedMatches.CountAsync(ct);
        var participants = await db.MatchParticipants
            .AsNoTracking()
            .Join(
                queueScopedMatches,
                participant => participant.MatchId,
                match => match.Id,
                (participant, _) => participant.Id)
            .CountAsync(ct);

        return cache.Store(cacheKey, new RawCorpusCounts(matches, participants), RawCorpusCountsTtl);
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

    /// <summary>
    /// The region-balance panel (#1153), degraded to its reason on failure like any signal: a
    /// broken per-region read must cost that panel, not the cockpit.
    /// </summary>
    private async Task<RegionBalanceReadModel> SafeRegionBalanceAsync(DateTime nowUtc, CancellationToken ct)
    {
        try
        {
            return await regionBalance.GetAsync(nowUtc, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Pipeline-health region balance failed to measure");

            return new RegionBalanceReadModel
            {
                UnknownReason = SanitizeError(ex.Message, environment.IsProduction()) ?? "internal error"
            };
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

    private sealed record RawCorpusCounts(int Matches, int Participants);
}
