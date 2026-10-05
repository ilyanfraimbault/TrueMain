using Core.Lol.Draft;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Draft;

namespace TrueMain.UnitTests;

public class DraftSuggestionScorerTests
{
    private static readonly DraftScoringWeights Weights = DraftScoringWeights.Default;

    private static DraftLaneRecord Record(params (int Opponent, double Delta, int Games)[] versus)
        => new(
            versus.Sum(v => v.Games),
            versus.Sum(v => v.Games) / 2,
            versus.ToDictionary(v => v.Opponent, v => new DraftComponent(v.Delta, v.Games)),
            versus.ToDictionary(v => v.Opponent, v => (Wins: v.Games / 2, Losses: v.Games / 4)),
            "16.19");

    private static readonly Dictionary<int, double> NoOccupancy = [];

    [Fact]
    public void BeforeTheEnemyPicksTheCardSaysItIsASafeBlind()
    {
        var record = Record((10, 0.03, 4_000), (11, 0.01, 3_000));
        var shares = new Dictionary<int, double> { [10] = 600, [11] = 400 };

        var candidate = DraftCandidateScorer.Score(1, record, NoOccupancy, shares, 0.5, [], Weights);

        var reason = Assert.Single(candidate.Reasons);
        Assert.Equal(DraftReasonKinds.BlindSafety, reason.Kind);
        Assert.Equal(0, reason.Count);
        Assert.Equal(2, reason.Of);
        Assert.True(candidate.Score > 0);
        Assert.Equal(7_000, candidate.Blind.Games);
    }

    [Fact]
    public void WithAKnownOpponentTheMatchupLeadsAndTheLanePhaseFollows()
    {
        var record = Record((10, 0.05, 2_000));
        var occupancy = new Dictionary<int, double> { [10] = 1d };
        var allies = new List<DraftAllyInput> { new(30, "UTILITY", Hovered: false, new DraftComponent(0.01, 500)) };

        var candidate = DraftCandidateScorer.Score(1, record, occupancy, NoOccupancy, 0.5, allies, Weights);

        Assert.Equal(DraftReasonKinds.LaneMatchup, candidate.Reasons[0].Kind);
        Assert.Equal(10, candidate.Reasons[0].ChampionId);
        Assert.Equal(1d, candidate.Reasons[0].Probability);
        Assert.Equal(DraftReasonKinds.Synergy, candidate.Reasons[1].Kind);
        Assert.Equal(DraftReasonKinds.LanePhase, candidate.Reasons[2].Kind);
        Assert.Equal(2d / 3d, candidate.Reasons[2].Rate!.Value, 9);
        Assert.Equal(0.05, candidate.MatchupDelta, 9);
    }

    [Fact]
    public void AHoveredAllyIsSaidToBeTentative()
    {
        var allies = new List<DraftAllyInput> { new(30, "UTILITY", Hovered: true, new DraftComponent(0.04, 5_000)) };

        var candidate = DraftCandidateScorer.Score(1, DraftLaneRecord.Empty, NoOccupancy, NoOccupancy, 0.5, allies, Weights);

        var reason = Assert.Single(candidate.Reasons);
        Assert.True(reason.Tentative);
        Assert.Equal("UTILITY", reason.Position);
    }

    [Fact]
    public void ACandidateWithNoDataIsThinAndCarriesNoReason()
    {
        var candidate = DraftCandidateScorer.Score(1, DraftLaneRecord.Empty, NoOccupancy, NoOccupancy, 0.5, [], Weights);

        Assert.True(candidate.ThinSample);
        Assert.Empty(candidate.Reasons);
        Assert.Equal(0d, candidate.Score);
    }

    [Fact]
    public void BansTargetThePlannedPicksWorstLikelyMatchups()
    {
        var records = new Dictionary<int, DraftLaneRecord>
        {
            // Our pick loses hard into 10 (common) and into 11 (rare), wins into 12.
            [1] = Record((10, -0.05, 5_000), (11, -0.08, 5_000), (12, 0.04, 5_000)),
        };
        var laneGames = new Dictionary<int, double> { [10] = 500, [11] = 20, [12] = 480 };

        var bans = DraftBanScorer.Rank([new DraftPoolEntry(1, 1)], records, laneGames, new HashSet<int>(), Weights);

        Assert.Equal([10, 11], bans.Select(b => b.ChampionId));
        var reason = Assert.Single(bans[0].Reasons);
        Assert.Equal(DraftReasonKinds.LaneThreat, reason.Kind);
        Assert.Equal(1, reason.ChampionId);
        Assert.Equal(0.5, reason.Share!.Value, 9);
    }

    [Fact]
    public void BansNeverSuggestAnExcludedChampion()
    {
        var records = new Dictionary<int, DraftLaneRecord> { [1] = Record((10, -0.05, 5_000)) };
        var laneGames = new Dictionary<int, double> { [10] = 500 };

        var bans = DraftBanScorer.Rank([new DraftPoolEntry(1, 1)], records, laneGames, new HashSet<int> { 10 }, Weights);

        Assert.Empty(bans);
    }

    [Fact]
    public void BanTargetsPreferTheDeclaredPickThenThePoolByMastery()
    {
        var pool = Enumerable.Range(1, 15).Select(id => new DraftPoolEntry(id, id * 1_000)).ToList();

        var (pick, pickTargets) = DraftBanQueryService.Targets(
            new DraftBanCriteria { Position = "MIDDLE", PlannedPick = 99, Pool = pool });
        var (poolTarget, poolTargets) = DraftBanQueryService.Targets(
            new DraftBanCriteria { Position = "MIDDLE", Pool = pool });
        var (none, _) = DraftBanQueryService.Targets(new DraftBanCriteria { Position = "MIDDLE" });

        Assert.Equal(DraftBanTargets.Pick, pick);
        Assert.Equal(99, Assert.Single(pickTargets).ChampionId);
        Assert.Equal(DraftBanTargets.Pool, poolTarget);
        Assert.Equal(DraftBanQueryService.MaxPoolTargets, poolTargets.Count);
        Assert.Equal(15, poolTargets[0].ChampionId);
        Assert.Equal(DraftBanTargets.None, none);
    }
}
