using Core.Options;
using Data;
using Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public sealed class PipelineHealthQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> mainAnalysisOptions) : IPipelineHealthQueryService
{
    private static readonly string[] ProcessNames =
    [
        "Discovery",
        "Scoring",
        "MatchIngestion",
        "MainAnalysis",
        "AccountRefresh",
        "RawDataRetention"
    ];

    public async Task<PipelineHealthReadModel> GetAsync(CancellationToken ct)
    {
        var queueId = mainAnalysisOptions.Value.QueueId;
        var latestRuns = await db.ProcessRuns
            .AsNoTracking()
            .Where(run => ProcessNames.Contains(run.ProcessName))
            .GroupBy(run => run.ProcessName)
            .Select(group => group
                .OrderByDescending(run => run.FinishedAtUtc)
                .First())
            .ToListAsync(ct);

        var queueScopedMatches = db.Matches
            .AsNoTracking()
            .Where(match => match.QueueId == queueId);

        var queueScopedMatchFreshness = await queueScopedMatches
            .Select(match => new
            {
                match.PlatformId,
                match.GameStartTimeUtc,
                match.GameVersion
            })
            .ToListAsync(ct);

        var platformFreshness = queueScopedMatchFreshness
            .GroupBy(match => match.PlatformId)
            .Select(group =>
            {
                var latestMatch = group
                    .OrderByDescending(match => match.GameStartTimeUtc)
                    .First();

                return new PlatformRawDataFreshnessReadModel
                {
                    PlatformId = group.Key,
                    LatestMatchStartAtUtc = latestMatch.GameStartTimeUtc,
                    LatestPatchVersion = NormalizePatchVersion(latestMatch.GameVersion)
                };
            })
            .OrderBy(platform => platform.PlatformId)
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
                            Error = run.Error
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

    private static string NormalizePatchVersion(string gameVersion)
    {
        if (string.IsNullOrWhiteSpace(gameVersion))
        {
            return string.Empty;
        }

        var segments = gameVersion.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length >= 2
            ? $"{segments[0]}.{segments[1]}"
            : gameVersion;
    }
}
