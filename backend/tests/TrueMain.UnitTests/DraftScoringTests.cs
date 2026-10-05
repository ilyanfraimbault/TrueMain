using Core.Lol.Draft;

namespace TrueMain.UnitTests;

public class DraftScoringTests
{
    private static readonly DraftScoringWeights Weights = DraftScoringWeights.Default;

    [Fact]
    public void ShrinkageKeepsALargeSampleAndDiscountsASmallOne()
    {
        var proven = new DraftComponent(0.03, 2_000);
        var thin = new DraftComponent(0.15, 20);

        Assert.Equal(0.03 * 2_000 / 2_100, proven.Shrunk(100), 9);
        Assert.Equal(0.15 * 20 / 120, thin.Shrunk(100), 9);
        Assert.True(proven.Shrunk(100) > thin.Shrunk(100));
        Assert.Equal(0d, DraftComponent.None.Shrunk(100));
    }

    [Fact]
    public void BlindSafetyWeighsOpponentsByHowOftenTheyArePlayed()
    {
        // Into the opponent played 90% of the time the pick is +4; into the rare
        // one, −10. The expectation leans on the common case.
        var versus = new Dictionary<int, DraftComponent>
        {
            [10] = new(0.04, 5_000),
            [11] = new(-0.10, 5_000),
        };
        var shares = new Dictionary<int, double> { [10] = 900, [11] = 100 };

        var blind = DraftScoring.Blind(versus, shares, 0);

        Assert.Equal((0.9 * 0.04) + (0.1 * -0.10), blind.Delta, 9);
        Assert.Equal(10_000, blind.Games);
        Assert.Equal(1, blind.LosingInto);
        Assert.Equal(2, blind.LikelyOpponents);
    }

    [Fact]
    public void BlindSafetyCountsAnUnseenOpponentAsNoEffectInTheRankingOnly()
    {
        var versus = new Dictionary<int, DraftComponent> { [10] = new(0.04, 5_000) };
        var shares = new Dictionary<int, double> { [10] = 500, [11] = 500 };

        var blind = DraftScoring.Blind(versus, shares, 0);

        // The measured figure is over what was measured; the ranking key halves it.
        Assert.Equal(0.04, blind.Delta, 9);
        Assert.Equal(0.02, blind.Shrunk, 9);
    }

    [Fact]
    public void BlindSafetyCountsTheLowerTailOverTheMostPlayedOpponentsOnly()
    {
        var versus = Enumerable.Range(1, 12).ToDictionary(id => id, _ => new DraftComponent(-0.05, 10_000));
        var shares = Enumerable.Range(1, 12).ToDictionary(id => id, id => (double)(100 - id));

        var blind = DraftScoring.Blind(versus, shares, 100);

        Assert.Equal(DraftScoring.LikelyOpponentCount, blind.LikelyOpponents);
        Assert.Equal(DraftScoring.LikelyOpponentCount, blind.LosingInto);
    }

    [Fact]
    public void ASplitLaneGuessWeighsBothReadingsInsteadOfBettingOnOne()
    {
        var versus = new Dictionary<int, DraftComponent>
        {
            [20] = new(0.06, 1_000),
            [21] = new(-0.06, 1_000),
        };
        var occupancy = new Dictionary<int, double> { [20] = 0.5, [21] = 0.5 };

        var (measured, shrunk, occupied) = DraftScoring.Lane(versus, occupancy, 100);

        Assert.Equal(0d, measured.Delta, 9);
        Assert.Equal(0d, shrunk, 9);
        Assert.Equal(1d, occupied, 9);
    }

    [Fact]
    public void AnEmptyLaneLeavesTheWholeLaneTermToTheBlindExpectation()
    {
        var score = DraftScoring.Score(laneShrunk: 0.05, laneOccupied: 0d, blindShrunk: 0.01, synergyShrunk: 0d, Weights);

        Assert.Equal(0.01, score, 9);
    }

    [Fact]
    public void SynergyIsAMeanSoFourAlliesDoNotOutweighOneLane()
    {
        var allies = Enumerable.Range(0, 4)
            .Select(_ => new DraftAllyPairing(new DraftComponent(0.02, 100_000), Hovered: false))
            .ToList();

        var (measured, shrunk) = DraftScoring.Synergy(allies, Weights);

        Assert.Equal(0.02, measured.Delta, 9);
        Assert.Equal(400_000, measured.Games);
        Assert.True(shrunk < 0.02);
    }

    [Fact]
    public void AHoveredAllyWeighsHalfALockedOne()
    {
        var allies = new List<DraftAllyPairing>
        {
            new(new DraftComponent(0.03, 1_000_000), Hovered: false),
            new(new DraftComponent(0d, 0), Hovered: true),
        };

        var (_, shrunk) = DraftScoring.Synergy(allies, Weights with { ShrinkGames = 0 });

        // (1 × 0.03 + 0.5 × 0) / 1.5
        Assert.Equal(0.02, shrunk, 9);
    }

    [Fact]
    public void AClearlyBetterMatchupIsNotOutrankedBySynergyAlone()
    {
        // #1713's acceptance: with the lane resolved, a +4 matchup against a
        // neutral one must hold against the best synergy the other pick can have
        // with every ally.
        var lane = DraftScoring.Score(0.04, 1d, 0d, 0d, Weights);
        var synergy = DraftScoring.Score(0d, 1d, 0d, 0.04, Weights);

        Assert.True(lane > synergy);
    }

    [Fact]
    public void ThreatIsHowFarBehindTimesHowLikely()
    {
        Assert.Equal(0.04 * 0.1, DraftScoring.Threat(new DraftComponent(-0.04, 1_000_000), 0.1, 0), 9);
        Assert.Equal(0d, DraftScoring.Threat(new DraftComponent(0.04, 1_000_000), 0.1, 0));
    }
}
