using AwesomeAssertions;
using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes;
using Ingestor.Processes.Components.Coverage;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TrueMain.UnitTests.Fixtures;

namespace TrueMain.UnitTests;

/// <summary>
/// Guards the weighted-sum arithmetic shared by both scoring branches (#497). The ladder
/// branch used to inline its own four-term normalised sum; these tests pin its results to
/// the pre-refactor formula, including the two edges the shared helper has to survive:
/// a zero rank + points weight (a valid configuration) and a zero total weight.
/// </summary>
public sealed class ScoringWeightedScoreTests
{
    private static readonly DateTime Now = new(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);

    private const int MaxLastPlayDays = 10;
    private const int TopChampionsPerAccount = 10;
    private const int CandidateChampionId = 22;
    private const int LastPlayDaysAgo = 2;
    private const int CandidateRankInMasteryTop = 3;
    private const long CandidateChampionPoints = 200_000;
    private const int CandidateObservedGames = 20;

    // Champion 22 has 5 of the 20 targeted mains => deficit 0.75, so the scarcity term is
    // neither 0 nor 1 and a dropped/misplaced scarcity weight cannot pass unnoticed.
    private static ChampionCoverageSnapshot Coverage =>
        new(new Dictionary<int, int> { [CandidateChampionId] = 5 }, targetMainsPerChampion: 20);

    public static TheoryData<double, double, double, double> WeightConfigurations => new()
    {
        // Production defaults.
        { 0.65, 0.20, 0.15, 0.25 },
        // Scarcity bonus disabled.
        { 0.65, 0.20, 0.15, 0.00 },
        // Merit weights both 0: the case that forbids collapsing rank + points into a
        // blended merit term (that would divide by rankWeight + pointsWeight => NaN).
        { 1.00, 0.00, 0.00, 0.00 },
        { 1.00, 0.00, 0.00, 0.50 },
        // Recency disabled, and weights that do not add up to 1.
        { 0.00, 0.50, 0.50, 0.00 },
        { 2.00, 1.00, 1.00, 0.00 },
        // All weights 0: startup validation rejects this, so it only happens when the
        // validator is bypassed. Exercises the defensive fallback.
        { 0.00, 0.00, 0.00, 0.00 }
    };

    [Theory]
    [MemberData(nameof(WeightConfigurations))]
    public async Task LadderCandidate_MatchesPreRefactorInlineFormula(
        double recencyWeight,
        double rankWeight,
        double pointsWeight,
        double scarcityWeight)
    {
        var scoring = BuildOptions(recencyWeight, rankWeight, pointsWeight, scarcityWeight);

        var score = await ScoreAsync(BuildLadderCandidate(), scoring);

        score.Should().BeApproximately(
            LegacyLadderScore(recencyWeight, rankWeight, pointsWeight, scarcityWeight),
            1e-9);
    }

    [Fact]
    public async Task LadderCandidate_WithZeroRankAndPointsWeights_StaysFinite()
    {
        // A valid configuration: startup validation only requires the four weights to sum
        // to > 0 (and scarcity <= recency + rank + points), so rank + points = 0 is allowed.
        var scoring = BuildOptions(recencyWeight: 1.0, rankWeight: 0, pointsWeight: 0, scarcityWeight: 0.5);

        var score = await ScoreAsync(BuildLadderCandidate(), scoring);

        double.IsNaN(score).Should().BeFalse();
        var expected = 100 * ((1.0 / 1.5) * RecencyScore + (0.5 / 1.5) * ScarcityScore);
        score.Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public async Task LadderCandidate_WithZeroTotalWeight_FallsBackToDefaultWeights()
    {
        var scoring = BuildOptions(recencyWeight: 0, rankWeight: 0, pointsWeight: 0, scarcityWeight: 0);

        var score = await ScoreAsync(BuildLadderCandidate(), scoring);

        // Unchanged from the inline formula: default weights, scarcity dropped.
        var expected = 100 * (0.65 * RecencyScore + 0.20 * RankScore + 0.15 * PointsScore);
        score.Should().BeApproximately(expected, 1e-9);
        score.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task HarvestCandidate_WithZeroTotalWeight_FallsBackToDefaultWeights()
    {
        // Reconciled with the ladder branch (#497): the fallback now runs before the source
        // split, so a bypassed validator degrades both branches the same way instead of
        // flattening every harvested candidate to 0.
        var scoring = BuildOptions(recencyWeight: 0, rankWeight: 0, pointsWeight: 0, scarcityWeight: 0);

        var score = await ScoreAsync(BuildHarvestCandidate(), scoring);

        var expected = 100 * (0.65 * RecencyScore + 0.35 * ObservedScore);
        score.Should().BeApproximately(expected, 1e-9);
        score.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task HarvestCandidate_WithZeroRankAndPointsWeights_ScoresOnRecencyAndScarcityOnly()
    {
        // Mirror of the ladder edge: the merit weight is rank + points, so it collapses to 0
        // here. The remaining terms must still be normalised over recency + scarcity.
        var scoring = BuildOptions(recencyWeight: 1.0, rankWeight: 0, pointsWeight: 0, scarcityWeight: 0.5);

        var score = await ScoreAsync(BuildHarvestCandidate(), scoring);

        double.IsNaN(score).Should().BeFalse();
        var expected = 100 * ((1.0 / 1.5) * RecencyScore + (0.5 / 1.5) * ScarcityScore);
        score.Should().BeApproximately(expected, 1e-9);
    }

    private static double RecencyScore => 1 - (double)LastPlayDaysAgo / MaxLastPlayDays;

    private static double RankScore => (TopChampionsPerAccount + 1 - CandidateRankInMasteryTop) / (double)TopChampionsPerAccount;

    private static double PointsScore => Math.Log10(CandidateChampionPoints + 1) / 6.0;

    private static double ScarcityScore => 0.75;

    private static double ObservedScore => Math.Log10(CandidateObservedGames + 1) / 1.5;

    /// <summary>
    /// The ladder formula exactly as it was inlined in <c>ComputeScore</c> before #497,
    /// defensive <c>weightSum &lt;= 0</c> fallback included.
    /// </summary>
    private static double LegacyLadderScore(
        double recencyWeight,
        double rankWeight,
        double pointsWeight,
        double scarcityWeight)
    {
        var weightSum = recencyWeight + rankWeight + pointsWeight + scarcityWeight;
        if (weightSum <= 0)
        {
            recencyWeight = 0.65;
            rankWeight = 0.20;
            pointsWeight = 0.15;
            scarcityWeight = 0;
            weightSum = 1.0;
        }

        return 100 * ((recencyWeight / weightSum) * RecencyScore
                      + (rankWeight / weightSum) * RankScore
                      + (pointsWeight / weightSum) * PointsScore
                      + (scarcityWeight / weightSum) * ScarcityScore);
    }

    private static ScoringOptions BuildOptions(
        double recencyWeight,
        double rankWeight,
        double pointsWeight,
        double scarcityWeight)
    {
        return new ScoringOptions
        {
            MaxLastPlayDays = MaxLastPlayDays,
            TopChampionsPerAccount = TopChampionsPerAccount,
            RecencyWeight = recencyWeight,
            RankWeight = rankWeight,
            PointsWeight = pointsWeight,
            ScarcityWeight = scarcityWeight
        };
    }

    private static MainCandidate BuildLadderCandidate()
    {
        return new MainCandidate
        {
            PlatformId = "KR",
            Puuid = "puuid-weighted-1",
            ChampionId = CandidateChampionId,
            Source = MainCandidateSource.Ladder,
            ChampionRankInMasteryTop = CandidateRankInMasteryTop,
            ChampionPoints = CandidateChampionPoints,
            LastPlayTimeUtc = Now.AddDays(-LastPlayDaysAgo),
            DiscoveredAtUtc = Now.AddHours(-1),
            Status = MainCandidateStatus.New
        };
    }

    private static MainCandidate BuildHarvestCandidate()
    {
        return new MainCandidate
        {
            PlatformId = "KR",
            Puuid = "puuid-weighted-2",
            ChampionId = CandidateChampionId,
            Source = MainCandidateSource.Harvest,
            ObservedGames = CandidateObservedGames,
            ObservedWins = CandidateObservedGames / 2,
            LastPlayTimeUtc = Now.AddDays(-LastPlayDaysAgo),
            DiscoveredAtUtc = Now.AddHours(-1),
            Status = MainCandidateStatus.New
        };
    }

    private static async Task<double> ScoreAsync(MainCandidate candidate, ScoringOptions scoring)
    {
        var sessionFactory = Substitute.For<IDataSessionFactory>();
        var session = Substitute.For<IDataSession>();
        var mainCandidates = Substitute.For<IMainCandidateRepository>();

        mainCandidates.GetNewBatchAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new List<MainCandidate> { candidate }),
                Task.FromResult(new List<MainCandidate>()));
        mainCandidates.GetScoredByPlatformAsync("KR", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<MainCandidate> { candidate }));

        session.MainCandidates.Returns(mainCandidates);
        sessionFactory.CreateAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(session));

        var coverageProvider = Substitute.For<IChampionCoverageProvider>();
        coverageProvider.GetSnapshotAsync(Arg.Any<IDataSession>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Coverage));

        var process = new ScoringProcess(
            NullLogger<ScoringProcess>.Instance,
            sessionFactory,
            coverageProvider,
            new FixedTimeProvider(Now),
            Microsoft.Extensions.Options.Options.Create(scoring));

        await process.RunCoreAsync(CancellationToken.None);

        return candidate.Score;
    }
}
