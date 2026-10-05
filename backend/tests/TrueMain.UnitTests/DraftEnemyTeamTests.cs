using Core.Lol.Draft;
using Core.Lol.Synergy;
using Data.Entities;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.Draft;

namespace TrueMain.UnitTests;

public class DraftEnemyTeamTests
{
    private static DraftEnemyReader.BaselineRow Self(int champion, bool own, int games, int wins, string patch = "16.19")
        => new(OpponentBaselineSide.Self, champion, own, patch, games, wins);

    private static DraftEnemyReader.BaselineRow Enemy(int champion, int games, int wins, string patch = "16.19")
        => new(OpponentBaselineSide.Enemy, champion, false, patch, games, wins);

    [Fact]
    public void TheEnemyTermIsAMeanWeightedByTheChanceTheEnemyIsOffOurLane()
    {
        var enemies = new List<DraftEnemyPairing>
        {
            new(new DraftComponent(0.04, 1_000_000), Weight: 1d),
            // Surely our lane opponent: the lane term has it, this one ignores it.
            new(new DraftComponent(-0.10, 1_000_000), Weight: 0d),
            new(DraftComponent.None, Weight: 1d),
        };

        var (measured, shrunk) = DraftScoring.Enemy(enemies, 0);

        Assert.Equal(0.04, measured.Delta, 9);
        Assert.Equal(0.02, shrunk, 9);
    }

    [Fact]
    public void TheEnemyTermOnlyMovesTheScoreWithAWeight()
    {
        var weights = DraftScoringWeights.Default;
        Assert.Equal(0d, weights.Enemy);
        Assert.Equal(
            DraftScoring.Score(0d, 1d, 0d, 0d, 0d, weights),
            DraftScoring.Score(0d, 1d, 0d, 0d, 0d, weights, enemyShrunk: 0.05));
        Assert.True(DraftScoring.Score(0d, 1d, 0d, 0d, 0d, weights with { Enemy = 0.5 }, enemyShrunk: 0.05) > 0d);
    }

    [Fact]
    public void ADeltaIsObservedMinusTheExpectationFromBothMarginals()
    {
        List<DraftEnemyReader.Row> pairs = [new(1, 64, "16.19", 400, 240)];
        List<DraftEnemyReader.BaselineRow> baselines =
        [
            Self(1, own: true, 5_000, 2_600),
            Self(2, own: true, 5_000, 2_500),
            Enemy(64, 4_000, 1_900),
        ];

        var deltas = DraftEnemyReader.Deltas(1, [64], pairs, baselines, new HashSet<string> { "16.19" });

        var expected = SynergyMath.ExpectedWinRate(0.52, [1_900d / 4_000], 5_100d / 10_000);
        Assert.Equal((240d / 400) - expected, deltas[64].Delta, 9);
        Assert.Equal(400, deltas[64].Games);
    }

    [Fact]
    public void AThinBaselineGivesNoDeltaRatherThanAnInventedOne()
    {
        List<DraftEnemyReader.Row> pairs = [new(1, 64, "16.19", 40, 30)];
        List<DraftEnemyReader.BaselineRow> baselines =
        [
            Self(1, own: true, 5_000, 2_600),
            Enemy(64, DraftEnemyReader.MinBaselineGames - 1, 20),
        ];

        Assert.Empty(DraftEnemyReader.Deltas(1, [64], pairs, baselines, null));
    }

    [Fact]
    public void AThinCurrentPatchReadsThePreviousOneToo()
    {
        var scope = new DraftPatchScope("16.19", "16.18");
        List<DraftEnemyReader.BaselineRow> thin = [Self(1, own: true, 50, 25)];
        List<DraftEnemyReader.BaselineRow> full = [Self(1, own: true, DraftLaneReader.MinCurrentPatchGames, 100)];

        Assert.Equal(2, DraftEnemyReader.PatchesFor(1, thin, scope)!.Count);
        Assert.Equal(["16.19"], DraftEnemyReader.PatchesFor(1, full, scope)!);
    }

    [Fact]
    public void WithAWeightTheEnemyTeamIsAReason()
    {
        var enemies = new List<DraftEnemyInput> { new(64, 0.9, new DraftComponent(-0.05, 50_000)) };

        var candidate = DraftCandidateScorer.Score(
            1,
            DraftLaneRecord.Empty,
            new Dictionary<int, double>(),
            new Dictionary<int, double>(),
            0.5,
            [],
            DraftScoringWeights.Default with { Enemy = 0.5 },
            enemies);

        var reason = Assert.Single(candidate.Reasons);
        Assert.Equal(DraftReasonKinds.EnemyTeam, reason.Kind);
        Assert.Equal(64, reason.ChampionId);
        Assert.Equal(-0.05, candidate.EnemyDelta, 9);
        Assert.True(candidate.Score < 0d);
    }
}
