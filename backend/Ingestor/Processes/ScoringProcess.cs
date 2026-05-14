using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public sealed class ScoringProcess(
    ILogger<ScoringProcess> logger,
    IDataSessionFactory sessionFactory,
    IOptions<ScoringOptions> scoringOptions) : IIngestorProcess
{
    /// <summary>
    /// Normalization factor for champion points logarithmic score.
    /// Based on Log10 of champion points, normalized so that approximately 1 million points equals a score of 1.0.
    /// Since Log10(1,000,000) ≈ 6, we divide by 6 to normalize the score to the [0, 1] range.
    /// </summary>
    private const double ChampionPointsLogNormalizer = 6.0;

    public string Name => "Scoring";

    public async Task<object?> RunCoreAsync(CancellationToken ct)
    {
        var scoring = scoringOptions.Value;

        await using var session = await sessionFactory.CreateAsync(ct);
        var scoringResult = await ScoreCandidatesAsync(session, scoring, ct);
        if (scoringResult.TotalScored == 0)
        {
            logger.LogInformation("No new candidates to score.");
            return new { reason = "No new candidates to score.", selected = 0 };
        }

        var platformSummaries = await PromoteTopCandidatesAsync(session, scoring, scoringResult.ScoredByPlatform, ct);
        return BuildSuccessPayload(platformSummaries);
    }

    private static async Task<ScoringResult> ScoreCandidatesAsync(
        IDataSession session,
        ScoringOptions scoring,
        CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var batchSize = Math.Max(1, scoring.BatchSize);
        var result = new ScoringResult();

        while (true)
        {
            var scoredCandidates = await ScoreCandidatesBatchAsync(session, scoring, nowUtc, batchSize, ct);
            if (scoredCandidates.Count == 0)
            {
                return result;
            }

            result.TotalScored += scoredCandidates.Count;

            foreach (var candidate in scoredCandidates)
            {
                result.ScoredByPlatform[candidate.PlatformId] = result.ScoredByPlatform.TryGetValue(candidate.PlatformId, out var count)
                    ? count + 1
                    : 1;
            }
        }
    }

    private static async Task<List<MainCandidate>> ScoreCandidatesBatchAsync(
        IDataSession session,
        ScoringOptions scoring,
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct)
    {
        var candidates = await session.MainCandidates.GetNewBatchAsync(batchSize, ct);
        if (candidates.Count == 0)
        {
            return [];
        }

        foreach (var candidate in candidates)
        {
            candidate.Score = ComputeScore(candidate, scoring, nowUtc);
            candidate.Status = MainCandidateStatus.Scored;
            candidate.ScoredAtUtc = nowUtc;
        }

        await session.SaveChangesAsync(ct);
        return candidates;
    }

    private async Task<List<object>> PromoteTopCandidatesAsync(
        IDataSession session,
        ScoringOptions scoring,
        IReadOnlyDictionary<string, int> scoredByPlatform,
        CancellationToken ct)
    {
        var platformSummaries = new List<object>();

        foreach (var platformId in scoredByPlatform.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            var queuedCandidates = await QueueTopCandidatesByPlatformAsync(session, platformId, scoring.TopNPerPlatform, ct);
            var scoredCount = scoredByPlatform[platformId];

            logger.LogInformation(
                "Scoring summary for {Platform}: scored={Scored}, queued={Queued}.",
                platformId,
                scoredCount,
                queuedCandidates.Count);

            platformSummaries.Add(new
            {
                platform = platformId,
                scored = scoredCount,
                queued = queuedCandidates.Count
            });
        }

        return platformSummaries;
    }

    private static async Task<IReadOnlyList<MainCandidate>> QueueTopCandidatesByPlatformAsync(
        IDataSession session,
        string platformId,
        int topNPerPlatform,
        CancellationToken ct)
    {
        var queuedCandidates = await session.MainCandidates.GetScoredByPlatformAsync(platformId, topNPerPlatform, ct);
        foreach (var candidate in queuedCandidates)
        {
            candidate.Status = MainCandidateStatus.Queued;
        }

        await session.SaveChangesAsync(ct);
        return queuedCandidates;
    }

    private static object BuildSuccessPayload(IEnumerable<object> platformSummaries)
    {
        return new { platforms = platformSummaries.ToList() };
    }

    private sealed class ScoringResult
    {
        public int TotalScored { get; set; }
        public Dictionary<string, int> ScoredByPlatform { get; } = new(StringComparer.OrdinalIgnoreCase);
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
