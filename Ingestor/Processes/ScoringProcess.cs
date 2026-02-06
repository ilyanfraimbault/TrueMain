using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Services;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public class ScoringProcess(
    ILogger<ScoringProcess> logger,
    IDataSessionFactory sessionFactory,
    ProcessRunRecorder runRecorder,
    IOptions<ScoringOptions> scoringOptions)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var scoring = scoringOptions.Value;
        var startedAt = DateTime.UtcNow;

        try
        {
            await using var session = await sessionFactory.CreateAsync(ct);
            var nowUtc = DateTime.UtcNow;

            var candidates = await session.MainCandidates.GetByStatusAsync(MainCandidateStatus.New, ct);

            if (candidates.Count == 0)
            {
                logger.LogInformation("No new candidates to score.");
                return;
            }

            foreach (var candidate in candidates)
            {
                candidate.Score = ComputeScore(candidate, scoring, nowUtc);
                candidate.Status = MainCandidateStatus.Scored;
                candidate.ScoredAtUtc = nowUtc;
            }

            await session.SaveChangesAsync(ct);

            var grouped = candidates
                .GroupBy(c => c.PlatformId, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var platformSummaries = new List<object>();

            foreach (var group in grouped)
            {
                var queued = await session.MainCandidates
                    .GetScoredByPlatformAsync(group.Key, scoring.TopNPerPlatform, ct);

                foreach (var candidate in queued)
                {
                    candidate.Status = MainCandidateStatus.Queued;
                }

                await session.SaveChangesAsync(ct);

                platformSummaries.Add(new
                {
                    platform = group.Key,
                    scored = group.Count(),
                    queued = queued.Count
                });

                logger.LogInformation(
                    "Scoring summary for {Platform}: scored={Scored}, queued={Queued}.",
                    group.Key,
                    group.Count(),
                    queued.Count);
            }

            var finishedAt = DateTime.UtcNow;
            await runRecorder.RecordAsync("Scoring", startedAt, finishedAt, ProcessRunStatus.Success,
                new { platforms = platformSummaries }, null, ct);
        }
        catch (Exception ex)
        {
            var finishedAt = DateTime.UtcNow;
            await runRecorder.RecordAsync("Scoring", startedAt, finishedAt, ProcessRunStatus.Failed, null, ex.Message, ct);
            throw;
        }
    }

    private static double ComputeScore(MainCandidate candidate, ScoringOptions scoring, DateTime nowUtc)
    {
        var maxLastPlayDays = scoring.MaxLastPlayDays <= 0 ? 1 : scoring.MaxLastPlayDays;
        var topN = scoring.TopChampionsPerAccount <= 0 ? 10 : scoring.TopChampionsPerAccount;

        var recencyDays = Math.Max(0, (nowUtc - candidate.LastPlayTimeUtc).TotalDays);
        var recencyScore = Clamp(1 - recencyDays / maxLastPlayDays, 0, 1);

        var rankScore = (topN + 1 - candidate.ChampionRankInMasteryTop) / (double)topN;
        rankScore = Clamp(rankScore, 0, 1);

        var pointsScore = Clamp(Math.Log10(candidate.ChampionPoints + 1) / 6, 0, 1);

        var recencyWeight = scoring.RecencyWeight;
        var rankWeight = scoring.RankWeight;
        var pointsWeight = scoring.PointsWeight;

        var weightSum = recencyWeight + rankWeight + pointsWeight;
        if (weightSum <= 0)
        {
            recencyWeight = 0.65;
            rankWeight = 0.20;
            pointsWeight = 0.15;
            weightSum = 1.0;
        }

        return 100 * ((recencyWeight / weightSum) * recencyScore
                      + (rankWeight / weightSum) * rankScore
                      + (pointsWeight / weightSum) * pointsScore);
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

}
