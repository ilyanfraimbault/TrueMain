using AwesomeAssertions;
using Core.Options;
using Core.Truemains;

namespace TrueMain.UnitTests;

/// <summary>
/// Pins the truemain score formula (#530, reworked in #1701). The score is
/// TrueMain's signature metric, so these tests fix both its boundaries (a
/// saturated one-trick reads 100, an empty player reads 0) and the ordering
/// properties the leaderboard relies on — the numbers here are the contract,
/// not an implementation detail.
/// </summary>
public sealed class DedicationScoreTests
{
    [Fact]
    public void Compute_returns_100_for_a_saturated_one_trick()
    {
        var result = DedicationScore.Compute(new DedicationInputs(
            PlayRate: 1d,
            MasteryPoints: DedicationScore.MasteryTargetPoints,
            MasteryRank: 1));

        result.Score.Should().Be(100d);
        result.Commitment.Should().Be(1d);
        result.Mastery.Should().Be(1d);
        result.MasteryRank.Should().Be(1d);
    }

    [Fact]
    public void Compute_returns_0_when_every_component_bottoms_out()
    {
        var result = DedicationScore.Compute(new DedicationInputs(
            PlayRate: DedicationScore.CommitmentFloor,
            MasteryPoints: DedicationScore.MasteryFloorPoints,
            MasteryRank: null));

        result.Score.Should().Be(0d);
        result.Commitment.Should().Be(0d);
        result.Mastery.Should().Be(0d);
        result.MasteryRank.Should().Be(0d);
    }

    [Fact]
    public void Compute_weights_sum_to_one_so_the_score_spans_the_full_scale()
    {
        var sum = DedicationScore.CommitmentWeight
                  + DedicationScore.MasteryWeight
                  + DedicationScore.MasteryRankWeight;

        sum.Should().BeApproximately(1d, 1e-9);
    }

    [Fact]
    public void Compute_never_leaves_the_0_100_range_on_out_of_range_inputs()
    {
        // Defensive: a play rate outside 0..1 or a nonsense rank can only come
        // from corrupt data, and must clamp rather than push the score off-scale.
        var overshoot = DedicationScore.Compute(new DedicationInputs(
            PlayRate: 4d,
            MasteryPoints: DedicationScore.MasteryTargetPoints * 10,
            MasteryRank: 1));

        var undershoot = DedicationScore.Compute(new DedicationInputs(
            PlayRate: -2d,
            MasteryPoints: -50,
            MasteryRank: -3));

        overshoot.Score.Should().Be(100d);
        undershoot.Score.Should().Be(0d);
    }

    [Fact]
    public void Compute_treats_NaN_play_rate_as_the_bottom_of_the_scale()
    {
        var result = DedicationScore.Compute(new DedicationInputs(double.NaN, null, null));

        result.Score.Should().Be(0d);
    }

    [Fact]
    public void Compute_is_monotone_in_play_rate()
    {
        var lower = DedicationScore.Compute(new DedicationInputs(0.4d, 500_000, 2));
        var higher = DedicationScore.Compute(new DedicationInputs(0.8d, 500_000, 2));

        higher.Score.Should().BeGreaterThan(lower.Score);
    }

    [Fact]
    public void Compute_is_monotone_in_mastery_points()
    {
        var lower = DedicationScore.Compute(new DedicationInputs(0.5d, 200_000, 1));
        var higher = DedicationScore.Compute(new DedicationInputs(0.5d, 1_500_000, 1));

        higher.Score.Should().BeGreaterThan(lower.Score);
    }

    [Fact]
    public void Compute_rewards_the_most_mastered_champion()
    {
        var second = DedicationScore.Compute(new DedicationInputs(0.5d, 800_000, 2));
        var first = DedicationScore.Compute(new DedicationInputs(0.5d, 800_000, 1));

        first.Score.Should().BeGreaterThan(second.Score);
    }

    [Fact]
    public void Mastery_is_logarithmic_between_the_floor_and_the_target()
    {
        DedicationScore.Mastery(DedicationScore.MasteryFloorPoints).Should().Be(0d);
        DedicationScore.Mastery(DedicationScore.MasteryTargetPoints).Should().BeApproximately(1d, 1e-12);

        // The geometric midpoint of floor and target reads one half: going from
        // 100k to 200k points counts as much as going from 1M to 2M.
        var midpoint = (long)Math.Sqrt((double)DedicationScore.MasteryFloorPoints * DedicationScore.MasteryTargetPoints);
        DedicationScore.Mastery(midpoint).Should().BeApproximately(0.5d, 1e-6);
    }

    [Fact]
    public void Mastery_reads_zero_when_not_read_yet()
    {
        DedicationScore.Mastery(null).Should().Be(0d);
    }

    [Theory]
    [InlineData(1, 1d)]
    [InlineData(2, 0.5d)]
    [InlineData(4, 0.25d)]
    public void MasteryRank_is_the_reciprocal_of_the_rank(int rank, double expected)
    {
        DedicationScore.MasteryRank(rank).Should().BeApproximately(expected, 1e-12);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public void MasteryRank_reads_zero_when_unknown(int? rank)
    {
        DedicationScore.MasteryRank(rank).Should().Be(0d);
    }

    [Fact]
    public void Compute_ranks_a_long_time_one_trick_above_a_fresh_pick_at_the_same_play_rate()
    {
        // The point of #1701: longevity is measured by the player's mastery, not
        // by how long TrueMain has been watching. A veteran one-trick outranks a
        // player who picked the champion up last month at the same play rate.
        var veteran = DedicationScore.Compute(new DedicationInputs(0.9d, 2_500_000, 1));
        var freshPick = DedicationScore.Compute(new DedicationInputs(0.9d, 90_000, 6));

        veteran.Score.Should().BeGreaterThan(freshPick.Score);
    }

    [Fact]
    public void Compute_keeps_play_rate_the_dominant_signal()
    {
        // Someone who gives the champion nine games in ten, with modest mastery,
        // is more of a truemain than a flex player with a huge lifetime pool on it.
        var oneTrick = DedicationScore.Compute(new DedicationInputs(0.95d, 300_000, 1));
        var flex = DedicationScore.Compute(new DedicationInputs(0.2d, 3_000_000, 1));

        oneTrick.Score.Should().BeGreaterThan(flex.Score);
    }

    [Fact]
    public void Compute_scores_unread_mastery_on_play_rate_alone()
    {
        var result = DedicationScore.Compute(new DedicationInputs(1d, null, null));

        result.Score.Should().BeApproximately(100d * DedicationScore.CommitmentWeight, 0.05d);
    }

    [Fact]
    public void Compute_rounds_the_score_to_one_decimal()
    {
        var result = DedicationScore.Compute(new DedicationInputs(0.537d, 612_345, 3));

        result.Score.Should().Be(Math.Round(result.Score, 1));
    }

    [Fact]
    public void CommitmentFloor_matches_the_main_analysis_play_rate_floor_default()
    {
        // #869: the commitment floor mirrors the play rate below which no
        // champion is classified as a main. Retuning the option's default
        // without the constant would silently hand the scale a dead band at the
        // bottom again.
        new MainAnalysisOptions().PlayRateFloor.Should().Be(DedicationScore.CommitmentFloor);
    }

    [Fact]
    public void Commitment_rescales_from_the_supplied_floor()
    {
        DedicationScore.Commitment(0.2d, commitmentFloor: 0.2d).Should().Be(0d);
        DedicationScore.Commitment(0.15d, commitmentFloor: 0.2d).Should().Be(0d);
        // (0.6 - 0.2) / (1 - 0.2) = 0.5, up to the usual binary-fraction wobble.
        DedicationScore.Commitment(0.6d, commitmentFloor: 0.2d).Should().BeApproximately(0.5d, 1e-12);
    }

    [Fact]
    public void Compute_threads_the_supplied_floor_into_commitment()
    {
        // The point of #869: a retuned MainAnalysis:PlayRateFloor must actually
        // move the score, otherwise the two drift apart with nothing failing.
        var inputs = new DedicationInputs(PlayRate: 0.3d, MasteryPoints: null, MasteryRank: null);

        var atDefaultFloor = DedicationScore.Compute(inputs);
        var atRaisedFloor = DedicationScore.Compute(inputs, commitmentFloor: 0.25d);

        atRaisedFloor.Commitment.Should().BeLessThan(atDefaultFloor.Commitment);
        atRaisedFloor.Score.Should().BeLessThan(atDefaultFloor.Score);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-0.1d)]
    [InlineData(1d)]
    [InlineData(1.5d)]
    public void Commitment_falls_back_to_the_default_floor_when_given_a_nonsense_one(double floor)
    {
        // A floor outside [0, 1) would invert the rescale or divide by zero.
        DedicationScore.Commitment(0.5d, floor)
            .Should().Be(DedicationScore.Commitment(0.5d));
    }
}
