using Core.Lol.Patches;
using Core.Options;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public sealed class PipelineHealthQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> mainAnalysisOptions,
    IHostEnvironment environment) : IPipelineHealthQueryService
{
    private static readonly string[] ProcessNames =
    [
        "Discovery",
        "Scoring",
        "MatchIngestion",
        "MainAnalysis",
        "ChampionPatternAggregation",
        "AccountRefresh",
        "MatchDataRetention"
    ];

    public async Task<PipelineHealthReadModel> GetAsync(CancellationToken ct)
    {
        var queueId = mainAnalysisOptions.Value.QueueId;

        // Postgres DISTINCT ON keeps the first row per ProcessName under the
        // ORDER BY, giving us the latest run per process in a single pass. This
        // translates to one server-side query instead of EF Core falling back
        // to a per-group correlated subquery (or client-side evaluation) for the
        // GroupBy(...).Select(g => g.OrderByDescending(...).First()) pattern.
        var latestRuns = await db.ProcessRuns
            .FromSqlInterpolated(
                $"""
                 SELECT DISTINCT ON ("ProcessName") *
                 FROM process_runs
                 WHERE "ProcessName" = ANY({ProcessNames})
                 ORDER BY "ProcessName", "FinishedAtUtc" DESC
                 """)
            .AsNoTracking()
            .ToListAsync(ct);

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

        var latestScopedRawMatchStartAtUtc = platformFreshness
            .Select(platform => platform.LatestMatchStartAtUtc)
            .Where(timestamp => timestamp.HasValue)
            .Select(timestamp => timestamp!.Value)
            .OrderByDescending(timestamp => timestamp)
            .FirstOrDefault();

        var latestChampionDataSignal = await db.MainChampionStats
            .AsNoTracking()
            .Select(stat => (DateTime?)stat.CalculatedAtUtc)
            .MaxAsync(ct);

        var latestMatchIngestionSuccess = latestRuns
            .Where(run => run.ProcessName == "MatchIngestion" && run.Status == ProcessRunStatus.Success)
            .Select(run => (DateTime?)run.FinishedAtUtc)
            .FirstOrDefault();

        var latestMainAnalysisSuccess = latestRuns
            .Where(run => run.ProcessName == "MainAnalysis" && run.Status == ProcessRunStatus.Success)
            .Select(run => (DateTime?)run.FinishedAtUtc)
            .FirstOrDefault();

        var sanitizeErrors = environment.IsProduction();

        return new PipelineHealthReadModel
        {
            Processes = ProcessNames
                .Select(processName =>
                {
                    var run = latestRuns.FirstOrDefault(candidate => candidate.ProcessName == processName);
                    return run is null
                        ? new ProcessHealthReadModel
                        {
                            ProcessName = processName,
                            Status = "missing"
                        }
                        : new ProcessHealthReadModel
                        {
                            ProcessName = run.ProcessName,
                            Status = run.Status.ToString().ToLowerInvariant(),
                            LastStartedAtUtc = run.StartedAtUtc,
                            LastFinishedAtUtc = run.FinishedAtUtc,
                            DurationMs = run.DurationMs,
                            Error = SanitizeError(run.Error, sanitizeErrors)
                        };
                })
                .ToList(),
            RawData = new RawDataFreshnessReadModel
            {
                QueueId = queueId,
                RawMatchCount = rawMatchCount,
                RawParticipantCount = rawParticipantCount,
                Platforms = platformFreshness
            },
            Gaps = new PipelineGapReadModel
            {
                MatchIngestionToMainAnalysisMinutes = ComputeGapMinutes(latestMatchIngestionSuccess, latestMainAnalysisSuccess),
                ChampionDataLagMinutes = ComputeGapMinutes(latestChampionDataSignal, latestScopedRawMatchStartAtUtc)
            }
        };
    }

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
