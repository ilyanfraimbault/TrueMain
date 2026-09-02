using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Components.Coverage;
using Ingestor.Processes.Components.Intake;
using Ingestor.Processes.Summaries;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public sealed class ScoringProcess(
    ILogger<ScoringProcess> logger,
    IDataSessionFactory sessionFactory,
    IChampionCoverageProvider coverageProvider,
    TimeProvider timeProvider,
    IOptions<ScoringOptions> scoringOptions,
    IOptions<MatchIngestionOptions> matchIngestionOptions,
    IOptions<IntakeOptions> intakeOptions) : IIngestorProcess
{
    // Weights used by the defensive fallback in ComputeScore when the configured ones sum to
    // <= 0. They mirror the ScoringOptions defaults, except scarcity which stays 0 so the
    // fallback ranks on merit alone. Startup validation makes that fallback unreachable in
    // production; it only guards a bypassed validator (e.g. a direct unit test).
    private const double FallbackRecencyWeight = 0.65;
    private const double FallbackRankWeight = 0.20;
    private const double FallbackPointsWeight = 0.15;

    public string Name => "Scoring";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var scoring = scoringOptions.Value;

        await using var session = await sessionFactory.CreateAsync(ct);
        var coverage = await coverageProvider.GetSnapshotAsync(session, ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var scoringResult = await ScoreCandidatesAsync(session, scoring, coverage, nowUtc, ct);
        if (scoringResult.TotalScored == 0)
        {
            logger.LogInformation("No new candidates to score.");
            return new NoWorkSummary("No new candidates to score.", 0);
        }

        // Sized by the claim, not by the ladder (#1361): promoting more per cycle than the
        // claim can absorb does not accelerate anything, it only deepens a queue whose head
        // never moves. Scoring:TopNPerPlatform stays the explicit ceiling; this is the
        // dynamic cap underneath it.
        var matchIngestion = matchIngestionOptions.Value;
        var promotionCap = IntakeCapacity.PromotionCapPerPlatform(
            matchIngestion,
            intakeOptions.Value,
            matchIngestion.Platforms.Count,
            scoring.TopNPerPlatform);

        logger.LogInformation(
            "Promotion cap per platform: {Cap} (claim capacity {Capacity} new account(s)/cycle x headroom {Headroom} over {Platforms} platform(s), ceiling Scoring:TopNPerPlatform={TopN}).",
            promotionCap,
            IntakeCapacity.NewCandidateSlotsPerCycle(matchIngestion),
            intakeOptions.Value.PromotionHeadroomFactor,
            matchIngestion.Platforms.Count,
            scoring.TopNPerPlatform);

        var platformSummaries = await PromoteTopCandidatesAsync(session, coverage, promotionCap, scoringResult.ScoredByPlatform, ct);
        return BuildSuccessPayload(platformSummaries, promotionCap);
    }

    private static async Task<ScoringResult> ScoreCandidatesAsync(
        IDataSession session,
        ScoringOptions scoring,
        ChampionCoverageSnapshot coverage,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var batchSize = Math.Max(1, scoring.BatchSize);
        var maxPerRun = Math.Max(0, scoring.MaxCandidatesPerRun);
        var result = new ScoringResult();

        // Capped per run like the aggregation folds (0 = drain everything, the shipped
        // default): harvest inserts thousands of candidates per run and resets refreshed
        // Scored candidates back to New, so an uncapped drain can spend a whole ingestor
        // tick scoring a backlog that is refilled on the next cycle anyway. What is left
        // is picked up by the next run.
        while (maxPerRun == 0 || result.TotalScored < maxPerRun)
        {
            ct.ThrowIfCancellationRequested();

            var take = maxPerRun == 0 ? batchSize : Math.Min(batchSize, maxPerRun - result.TotalScored);
            var scoredByPlatform = await ScoreCandidatesBatchAsync(session, scoring, coverage, nowUtc, take, ct);
            if (scoredByPlatform.Count == 0)
            {
                return result;
            }

            foreach (var (platformId, count) in scoredByPlatform)
            {
                result.TotalScored += count;
                result.ScoredByPlatform[platformId] = result.ScoredByPlatform.TryGetValue(platformId, out var running)
                    ? running + count
                    : count;
            }
        }

        return result;
    }

    /// <summary>
    /// Scores one batch and returns its per-platform counts — counts, not the entities,
    /// so nothing survives the <c>ClearTracking</c> below (#1229).
    /// </summary>
    private static async Task<Dictionary<string, int>> ScoreCandidatesBatchAsync(
        IDataSession session,
        ScoringOptions scoring,
        ChampionCoverageSnapshot coverage,
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct)
    {
        var candidates = await session.MainCandidates.GetNewBatchAsync(batchSize, ct);
        if (candidates.Count == 0)
        {
            return [];
        }

        var scoredByPlatform = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            candidate.Score = ComputeScore(candidate, scoring, coverage, nowUtc);
            candidate.Status = MainCandidateStatus.Scored;
            candidate.ScoredAtUtc = nowUtc;
            scoredByPlatform[candidate.PlatformId] = scoredByPlatform.TryGetValue(candidate.PlatformId, out var count)
                ? count + 1
                : 1;
        }

        await session.SaveChangesAsync(ct);

        // Drain the change tracker between batches. This loop drains the whole New
        // backlog — 100k rows in batches of 5 000 — and every candidate stayed tracked
        // to the end, so each SaveChanges re-ran DetectChanges over every entity the
        // run had ever touched: quadratic in the number of batches (#1229). Safe here
        // because the batch is re-read from the database each time and nothing loaded
        // before the clear is touched after it — the caller now gets counts, and
        // PromoteTopCandidatesAsync loads its own candidates further down.
        session.ClearTracking();
        return scoredByPlatform;
    }

    private async Task<List<ScoringPlatformSummary>> PromoteTopCandidatesAsync(
        IDataSession session,
        ChampionCoverageSnapshot coverage,
        int promotionCap,
        IReadOnlyDictionary<string, int> scoredByPlatform,
        CancellationToken ct)
    {
        var platformSummaries = new List<ScoringPlatformSummary>();

        // Depth over breadth (#900): a champion already at the coverage target does not
        // need another main more than an under-covered one does, so its candidates only
        // take the top-N slots the under-covered champions leave. Deliberately a
        // priority, not a filter — with a small scored pool a saturated champion still
        // gets promoted rather than wasting the platform's queue.
        //
        // Saturation is read per platform (#1150): a champion covered on EUW1 is not
        // covered on KR, and deprioritising its KR candidates because of EUW1's mains is
        // exactly how the under-served region stayed under-served.
        foreach (var platformId in scoredByPlatform.Keys.Order(StringComparer.OrdinalIgnoreCase))
        {
            var queuedCandidates = await QueueTopCandidatesByPlatformAsync(
                session,
                platformId,
                promotionCap,
                coverage.SaturatedChampionIdsFor(platformId).ToList(),
                ct);
            var scoredCount = scoredByPlatform[platformId];

            logger.LogInformation(
                "Scoring summary for {Platform}: scored={Scored}, queued={Queued}.",
                platformId,
                scoredCount,
                queuedCandidates.Count);

            platformSummaries.Add(new ScoringPlatformSummary(platformId, scoredCount, queuedCandidates.Count));
        }

        return platformSummaries;
    }

    private static async Task<IReadOnlyList<MainCandidate>> QueueTopCandidatesByPlatformAsync(
        IDataSession session,
        string platformId,
        int promotionCap,
        IReadOnlyCollection<int> saturatedChampionIds,
        CancellationToken ct)
    {
        var queuedCandidates = await session.MainCandidates
            .GetScoredByPlatformAsync(platformId, promotionCap, saturatedChampionIds, ct);
        foreach (var candidate in queuedCandidates)
        {
            candidate.Status = MainCandidateStatus.Queued;
        }

        await session.SaveChangesAsync(ct);
        return queuedCandidates;
    }

    private static ScoringSummary BuildSuccessPayload(
        IEnumerable<ScoringPlatformSummary> platformSummaries,
        int promotionCap)
    {
        return new ScoringSummary(platformSummaries.ToList(), promotionCap);
    }

    private sealed class ScoringResult
    {
        public int TotalScored { get; set; }
        public Dictionary<string, int> ScoredByPlatform { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static double ComputeScore(
        MainCandidate candidate,
        ScoringOptions scoring,
        ChampionCoverageSnapshot coverage,
        DateTime nowUtc)
    {
        var maxLastPlayDays = scoring.MaxLastPlayDays <= 0 ? 1 : scoring.MaxLastPlayDays;
        var topN = scoring.TopChampionsPerAccount <= 0 ? 10 : scoring.TopChampionsPerAccount;

        var recencyDays = Math.Max(0, (nowUtc - candidate.LastPlayTimeUtc).TotalDays);
        var recencyScore = Clamp(1 - recencyDays / maxLastPlayDays, 0, 1);

        var recencyWeight = scoring.RecencyWeight;
        var rankWeight = scoring.RankWeight;
        var pointsWeight = scoring.PointsWeight;
        var scarcityWeight = Math.Max(0, scoring.ScarcityWeight);
        var scarcityScore = coverage.Deficit(candidate.PlatformId, candidate.ChampionId);

        // Defensive only: startup validation guarantees the four weights sum to > 0, so this
        // branch is unreachable in production. It guards against a divide-by-zero if the
        // validator is ever bypassed (e.g. a future refactor or a direct test). Substituting
        // the documented defaults keeps a usable ranking, where a flat 0 would make every
        // candidate tie. Applied before the source split so both branches degrade the same way
        // (#497) — the ladder branch used to own this fallback while the harvest branch fell
        // back to a 0 score.
        if (recencyWeight + rankWeight + pointsWeight + scarcityWeight <= 0)
        {
            recencyWeight = FallbackRecencyWeight;
            rankWeight = FallbackRankWeight;
            pointsWeight = FallbackPointsWeight;
            scarcityWeight = 0;
        }

        // Harvested candidates (#485) have no mastery rank/points — only observed games
        // from orphan participant rows. They reuse the combined rank+points weight as a
        // single observed-games merit term, keeping the same weight denominator (and so the
        // same 0-100 scale) as ladder candidates. The sample is a biased prior, not a main
        // verdict; final confirmation still comes from history ingestion + MainAnalysis.
        //
        // Only ObservedGames feeds the merit here. ObservedWins is persisted as the observed
        // winrate signal (per #485) for completeness and the candidate read model, but is
        // intentionally NOT a scoring input yet — winrate weighting is a deliberate future
        // refinement, kept out of this first scorer to avoid over-fitting a biased sample.
        if (candidate.Source == MainCandidateSource.Harvest)
        {
            // Defensive only: startup validation guarantees HarvestObservedGamesLogNormalizer > 0,
            // so the 1.5 fallback is unreachable in production (matches the topN/maxLastPlayDays
            // guards above). It keeps a direct unit test that bypasses validation from dividing by 0.
            var normalizer = scoring.HarvestObservedGamesLogNormalizer <= 0 ? 1.5 : scoring.HarvestObservedGamesLogNormalizer;
            var observedScore = Clamp(Math.Log10(candidate.ObservedGames + 1) / normalizer, 0, 1);
            var meritWeight = rankWeight + pointsWeight;

            return ComputeWeightedScore(
                recencyWeight, recencyScore,
                meritWeight, observedScore,
                scarcityWeight, scarcityScore);
        }

        var rankScore = (topN + 1 - candidate.ChampionRankInMasteryTop) / (double)topN;
        rankScore = Clamp(rankScore, 0, 1);

        // Defensive only, exactly like the harvest normalizer above: startup validation
        // guarantees Scoring:ChampionPointsLogNormalizer > 0, so the 6.0 fallback is
        // unreachable in production and only guards a test that bypasses the validator.
        var pointsNormalizer = scoring.ChampionPointsLogNormalizer <= 0 ? 6.0 : scoring.ChampionPointsLogNormalizer;
        var pointsScore = Clamp(Math.Log10(candidate.ChampionPoints + 1) / pointsNormalizer, 0, 1);

        return ComputeWeightedScore(
            recencyWeight, recencyScore,
            rankWeight, rankScore,
            pointsWeight, pointsScore,
            scarcityWeight, scarcityScore);
    }

    /// <summary>
    /// Weighted blend of the ladder terms (recency, rank, points, scarcity) on a 0-100 scale,
    /// normalised by the weight sum. Single source of the weighted-sum arithmetic: the harvest
    /// branch reaches it through the three-term overload below (#497).
    /// </summary>
    /// <remarks>
    /// Including scarcityWeight in the denominator is deliberate: it normalises the total
    /// while compressing covered-champion scores proportionally, which is what gives
    /// under-covered champions their relative boost. With defaults (0.65+0.20+0.15+0.25=1.25)
    /// a fully-covered champion (deficit 0) tops out at ~80, while an under-covered one
    /// (deficit 1) can reach 100. ScarcityWeight is validated at startup to not exceed the
    /// combined merit weights (recency + rank + points), so scarcity cannot outweigh merit.
    /// </remarks>
    private static double ComputeWeightedScore(
        double recencyWeight, double recencyScore,
        double rankWeight, double rankScore,
        double pointsWeight, double pointsScore,
        double scarcityWeight, double scarcityScore)
    {
        var weightSum = recencyWeight + rankWeight + pointsWeight + scarcityWeight;

        // Defensive only: ComputeScore already substitutes the fallback weights when the
        // configured ones sum to <= 0, so neither branch can reach this. It stays as the
        // last divide-by-zero guard of the formula itself.
        if (weightSum <= 0)
        {
            return 0;
        }

        return 100 * ((recencyWeight / weightSum) * recencyScore
                      + (rankWeight / weightSum) * rankScore
                      + (pointsWeight / weightSum) * pointsScore
                      + (scarcityWeight / weightSum) * scarcityScore);
    }

    /// <summary>
    /// Three-term form for candidates whose merit is a single signal (harvest: observed games),
    /// so they land on the same 0-100 scale as ladder candidates.
    /// </summary>
    /// <remarks>
    /// The merit term simply takes the rank slot with a zero-weight points term, which keeps the
    /// denominator at recency + merit + scarcity. It is deliberately NOT the mirror operation —
    /// collapsing the ladder's rank + points into a blended merit score would have to divide by
    /// rankWeight + pointsWeight, and a valid configuration may set both to 0 (startup validation
    /// only guarantees the four weights sum to > 0), yielding NaN. See #497.
    /// </remarks>
    private static double ComputeWeightedScore(
        double recencyWeight, double recencyScore,
        double meritWeight, double meritScore,
        double scarcityWeight, double scarcityScore)
    {
        return ComputeWeightedScore(
            recencyWeight, recencyScore,
            meritWeight, meritScore,
            0, 0,
            scarcityWeight, scarcityScore);
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
