using AwesomeAssertions;
using Core.Lol.Lane;
using Ingestor.Options;
using TrueMain.Options;

namespace TrueMain.UnitTests;

/// <summary>
/// Pins the one rule two consumers judge lanes with (#1117): the ingestor's
/// <c>ChampionLaneOutcomeAggregationProcess</c>, which folds the stored counters, and the
/// API's live pass over a composition's sampled games. Both already have integration
/// suites, but they exercise <see cref="LaneOutcomeRules.Judge"/> through a full fold,
/// where a flip on the strict comparison is absorbed by whichever gold gaps the fixtures
/// happened to seed. The boundary belongs in a pure test that names it.
/// </summary>
public class LaneOutcomeRulesTests
{
    [Theory]
    [InlineData(301)]
    [InlineData(500)]
    [InlineData(10_000)]
    public void Judge_calls_a_lane_won_only_strictly_above_the_threshold(int goldDiff)
    {
        LaneOutcomeRules.Judge(goldDiff, LaneOutcomeRules.DefaultGoldLeadThreshold)
            .Should().Be(LaneStanding.Won);
    }

    [Theory]
    [InlineData(-301)]
    [InlineData(-500)]
    [InlineData(-10_000)]
    public void Judge_calls_a_lane_lost_only_strictly_below_the_negated_threshold(int goldDiff)
    {
        LaneOutcomeRules.Judge(goldDiff, LaneOutcomeRules.DefaultGoldLeadThreshold)
            .Should().Be(LaneStanding.Lost);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(299)]
    [InlineData(-299)]
    public void Judge_leaves_the_band_in_the_middle_undecided(int goldDiff)
    {
        // The band is a third outcome on purpose: folding it into losses would print
        // "lane lost" where nothing was settled.
        LaneOutcomeRules.Judge(goldDiff, LaneOutcomeRules.DefaultGoldLeadThreshold)
            .Should().Be(LaneStanding.Even);
    }

    /// <summary>
    /// The documented edge case, in the direction the comment states: the comparison is
    /// strict, so a gap of exactly the threshold is even, not won.
    /// </summary>
    [Fact]
    public void Judge_treats_exactly_the_threshold_as_even_rather_than_won()
    {
        LaneOutcomeRules.Judge(300, 300).Should().Be(LaneStanding.Even);
        LaneOutcomeRules.Judge(1, 1).Should().Be(LaneStanding.Even);
    }

    /// <summary>
    /// The mirror of the above, which a one-sided <c>&gt;=</c> slip would break on its own:
    /// exactly minus the threshold is even, not lost.
    /// </summary>
    [Fact]
    public void Judge_treats_exactly_minus_the_threshold_as_even_rather_than_lost()
    {
        LaneOutcomeRules.Judge(-300, 300).Should().Be(LaneStanding.Even);
        LaneOutcomeRules.Judge(-1, 1).Should().Be(LaneStanding.Even);
    }

    [Fact]
    public void Judge_is_symmetric_so_neither_side_of_a_lane_is_favoured()
    {
        // The same lane read from the opponent's seat must give the mirrored verdict;
        // an asymmetric band would let both players' pages call the lane won.
        foreach (var goldDiff in new[] { 0, 1, 299, 300, 301, 5_000 })
        {
            var mirrored = LaneOutcomeRules.Judge(-goldDiff, 300);
            var expected = LaneOutcomeRules.Judge(goldDiff, 300) switch
            {
                LaneStanding.Won => LaneStanding.Lost,
                LaneStanding.Lost => LaneStanding.Won,
                _ => LaneStanding.Even,
            };

            mirrored.Should().Be(expected, "a lane judged from the other seat must mirror");
        }
    }

    [Fact]
    public void Judge_with_a_zero_threshold_decides_every_non_zero_gap()
    {
        // A deployment that zeroes the threshold gets a two-outcome rule, and a dead-even
        // lane is still the one case that belongs in neither counter.
        LaneOutcomeRules.Judge(1, 0).Should().Be(LaneStanding.Won);
        LaneOutcomeRules.Judge(-1, 0).Should().Be(LaneStanding.Lost);
        LaneOutcomeRules.Judge(0, 0).Should().Be(LaneStanding.Even);
    }

    /// <summary>
    /// The anti-drift guard. Neither consumer reads
    /// <see cref="LaneOutcomeRules.DefaultGoldLeadThreshold"/> at judgement time — each
    /// binds its own option — so nothing else in the build fails when one of the two
    /// defaults is edited in isolation. That edit is exactly how the champion page and
    /// the matchup tool would start meaning different things by "the lane was won".
    /// </summary>
    [Fact]
    public void Both_consumers_default_to_the_shared_threshold()
    {
        new LaneOutcomeAggregationOptions().GoldLeadThreshold
            .Should().Be(LaneOutcomeRules.DefaultGoldLeadThreshold);

        new CompositionSearchOptions().LaneGoldLeadThreshold
            .Should().Be(LaneOutcomeRules.DefaultGoldLeadThreshold);
    }
}
