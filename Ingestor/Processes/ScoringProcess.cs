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
    /// <summary>
    /// Normalization factor for champion points logarithmic score.
    /// Based on Log10 of champion points, normalized so that approximately 1 million points equals a score of 1.0.
    /// Since Log10(1,000,000) ≈ 6, we divide by 6 to normalize the score to the [0, 1] range.
    /// </summary>
    private const double ChampionPointsLogNormalizer = 6.0;

    public async Task RunAsync(CancellationToken ct)
    {
        var scoring = scoringOptions.Value;
        var startedAt = DateTime.UtcNow;

        try
        {
            await using var session = await sessionFactory.CreateAsync(ct);
            var nowUtc = DateTime.UtcNow;
            var batchSize = Math.Max(1, scoring.BatchSize);

            var totalScored = 0;
            var platformsTouched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var scoredByPlatform = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            while (true)
            {
                var batch = await session.MainCandidates.GetNewBatchAsync(batchSize, ct);
                if (batch.Count == 0)
                {
                    break;
                }

                foreach (var candidate in batch)
                {
                    candidate.Score = ComputeScore(candidate, scoring, nowUtc);
                    candidate.Status = MainCandidateStatus.Scored;
                    candidate.ScoredAtUtc = nowUtc;
                    platformsTouched.Add(candidate.PlatformId);
                    scoredByPlatform[candidate.PlatformId] = scoredByPlatform.TryGetValue(candidate.PlatformId, out var count)
                        ? count + 1
                        : 1;
                }

                totalScored += batch.Count;
                await session.SaveChangesAsync(ct);
            }

            if (totalScored == 0)
            {
                logger.LogInformation("No new candidates to score.");
                return;
            }

            var platformSummaries = new List<object>();

            foreach (var platformId in platformsTouched)
            {
                var queued = await session.MainCandidates
                    .GetScoredByPlatformAsync(platformId, scoring.TopNPerPlatform, ct);

                foreach (var candidate in queued)
                {
                    candidate.Status = MainCandidateStatus.Queued;
                }

                await session.SaveChangesAsync(ct);

                platformSummaries.Add(new
                {
                    platform = platformId,
                    scored = scoredByPlatform.TryGetValue(platformId, out var scoredCount) ? scoredCount : 0,
                    queued = queued.Count
                });

                logger.LogInformation(
                    "Scoring summary for {Platform}: scored={Scored}, queued={Queued}.",
                    platformId,
                    scoredByPlatform.TryGetValue(platformId, out var scored) ? scored : 0,
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

        var pointsScore = Clamp(Math.Log10(candidate.ChampionPoints + 1) / ChampionPointsLogNormalizer, 0, 1);

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
