using AwesomeAssertions;
using Core.Truemains;

namespace TrueMain.UnitTests;

/// <summary>
/// Pins the dedication formula (#530). The score is TrueMain's signature
/// metric, so these tests fix both its boundaries (a perfect one-trick reads
/// 100, an empty player reads 0) and the ordering properties the leaderboard
/// relies on — the numbers here are the contract, not an implementation detail.
/// </summary>
public sealed class DedicationScoreTests
{
    /// <summary>The theoretical maximum: pure one-trick, saturated span, saturated volume, played today.</summary>
    private static DedicationInputs PerfectInputs() => new(
        PlayRate: 1d,
        CareerGames: DedicationScore.VolumeTargetGames,
        PatchSpan: DedicationScore.SpanTargetPatches,
        DaysSinceLastGame: 0d);

    [Fact]
    public void Compute_returns_100_for_a_saturated_one_trick()
    {
        var result = DedicationScore.Compute(PerfectInputs());

        result.Score.Should().Be(100d);
        result.Commitment.Should().Be(1d);
        result.Span.Should().Be(1d);
        result.Volume.Should().Be(1d);
        result.Recency.Should().Be(1d);
    }

    [Fact]
    public void Compute_returns_0_when_every_component_bottoms_out()
    {
        var result = DedicationScore.Compute(new DedicationInputs(
            PlayRate: DedicationScore.CommitmentFloor,
            CareerGames: 0,
            PatchSpan: 0,
            DaysSinceLastGame: double.PositiveInfinity));

        result.Score.Should().Be(0d);
        result.Commitment.Should().Be(0d);
        result.Span.Should().Be(0d);
        result.Volume.Should().Be(0d);
        result.Recency.Should().Be(0d);
    }

    [Fact]
    public void Compute_weights_sum_to_one_so_the_score_spans_the_full_scale()
    {
        var sum = DedicationScore.CommitmentWeight
                  + DedicationScore.SpanWeight
                  + DedicationScore.VolumeWeight
                  + DedicationScore.RecencyWeight;

        sum.Should().BeApproximately(1d, 1e-9);
    }

    [Fact]
    public void Compute_never_leaves_the_0_100_range_on_out_of_range_inputs()
    {
        // Defensive: a play rate above 1 or a negative day count can only come
        // from corrupt data, and must clamp rather than push the score off-scale.
        var overshoot = DedicationScore.Compute(new DedicationInputs(
            PlayRate: 4d,
            CareerGames: DedicationScore.VolumeTargetGames * 10,
            PatchSpan: DedicationScore.SpanTargetPatches * 10,
            DaysSinceLastGame: -500d));

        var undershoot = DedicationScore.Compute(new DedicationInputs(
            PlayRate: -2d,
            CareerGames: -50,
            PatchSpan: -3,
            DaysSinceLastGame: 10_000d));

        overshoot.Score.Should().Be(100d);
        undershoot.Score.Should().Be(0d);
    }

    [Fact]
    public void Compute_treats_NaN_inputs_as_the_bottom_of_the_scale()
    {
        // A NaN would otherwise poison the weighted sum and make the whole score
        // NaN, which serialises as null and breaks the sort.
        var result = DedicationScore.Compute(new DedicationInputs(
            PlayRate: double.NaN,
            CareerGames: 0,
            PatchSpan: 0,
            DaysSinceLastGame: double.NaN));

        result.Score.Should().Be(0d);
        result.Commitment.Should().Be(0d);
        result.Recency.Should().Be(0d);
    }

    [Fact]
    public void Compute_is_monotone_in_play_rate()
    {
        var low = DedicationScore.Compute(PerfectInputs() with { PlayRate = 0.3d });
        var mid = DedicationScore.Compute(PerfectInputs() with { PlayRate = 0.6d });
        var high = DedicationScore.Compute(PerfectInputs() with { PlayRate = 0.9d });

        high.Score.Should().BeGreaterThan(mid.Score);
        mid.Score.Should().BeGreaterThan(low.Score);
    }

    [Fact]
    public void Compute_is_monotone_in_career_games()
    {
        var few = DedicationScore.Compute(PerfectInputs() with { CareerGames = 20 });
        var many = DedicationScore.Compute(PerfectInputs() with { CareerGames = 150 });

        many.Score.Should().BeGreaterThan(few.Score);
    }

    [Fact]
    public void Compute_is_monotone_in_patch_span()
    {
        var single = DedicationScore.Compute(PerfectInputs() with { PatchSpan = 1 });
        var several = DedicationScore.Compute(PerfectInputs() with { PatchSpan = 4 });

        several.Score.Should().BeGreaterThan(single.Score);
    }

    [Fact]
    public void Compute_decays_with_inactivity()
    {
        var today = DedicationScore.Compute(PerfectInputs());
        var lastWeek = DedicationScore.Compute(PerfectInputs() with { DaysSinceLastGame = 7d });
        var lastQuarter = DedicationScore.Compute(PerfectInputs() with { DaysSinceLastGame = 90d });

        today.Score.Should().BeGreaterThan(lastWeek.Score);
        lastWeek.Score.Should().BeGreaterThan(lastQuarter.Score);
    }

    [Fact]
    public void Recency_halves_at_the_half_life()
    {
        DedicationScore.Recency(DedicationScore.RecencyHalfLifeDays)
            .Should().BeApproximately(0.5d, 1e-9);

        DedicationScore.Recency(2 * DedicationScore.RecencyHalfLifeDays)
            .Should().BeApproximately(0.25d, 1e-9);
    }

    [Fact]
    public void Recency_treats_a_future_timestamp_as_played_now()
    {
        // Clock skew between Riot's game timestamp and the API host must not
        // hand out a recency above 1.
        DedicationScore.Recency(-3d).Should().Be(1d);
    }

    [Fact]
    public void Commitment_reads_zero_at_the_main_analysis_play_rate_floor()
    {
        // Below the floor no champion is classified as a main, so the scale
        // starts there rather than at 0 — otherwise the bottom eighth of the
        // range would be unreachable.
        DedicationScore.Commitment(DedicationScore.CommitmentFloor).Should().Be(0d);
        DedicationScore.Commitment(DedicationScore.CommitmentFloor - 0.05d).Should().Be(0d);
        DedicationScore.Commitment(1d).Should().Be(1d);
    }

    [Fact]
    public void Volume_is_logarithmic_so_early_games_count_for_more()
    {
        // The first 20 games must move the needle more than games 180→200, or
        // the component would just re-measure raw activity.
        var firstStep = DedicationScore.Volume(20) - DedicationScore.Volume(0);
        var lastStep = DedicationScore.Volume(200) - DedicationScore.Volume(180);

        firstStep.Should().BeGreaterThan(lastStep);
    }

    [Fact]
    public void Compute_ranks_a_long_term_one_trick_above_a_recent_flavour_of_the_month()
    {
        // The metric's whole point: 300 games across 8 patches on one champion
        // beats a burst of 30 games on a single patch, even though both players
        // played today and both are near-pure one-tricks.
        var oneTrick = DedicationScore.Compute(new DedicationInputs(
            PlayRate: 0.92d, CareerGames: 300, PatchSpan: 8, DaysSinceLastGame: 1d));
        var flavourOfTheMonth = DedicationScore.Compute(new DedicationInputs(
            PlayRate: 0.92d, CareerGames: 30, PatchSpan: 1, DaysSinceLastGame: 1d));

        oneTrick.Score.Should().BeGreaterThan(flavourOfTheMonth.Score);
    }

    [Fact]
    public void Compute_keeps_a_dormant_veteran_ahead_of_an_uncommitted_active_player()
    {
        // Recency carries the smallest weight on purpose: a month off must not
        // sink a career one-trick below someone who barely plays the champion.
        // This is also what keeps an ingestion stall from scrambling the board.
        var dormantVeteran = DedicationScore.Compute(new DedicationInputs(
            PlayRate: 0.95d, CareerGames: 400, PatchSpan: 9, DaysSinceLastGame: 30d));
        var activeDabbler = DedicationScore.Compute(new DedicationInputs(
            PlayRate: 0.22d, CareerGames: 25, PatchSpan: 2, DaysSinceLastGame: 0d));

        dormantVeteran.Score.Should().BeGreaterThan(activeDabbler.Score);
    }

    [Fact]
    public void Compute_scores_a_missing_aggregate_history_on_commitment_alone()
    {
        // A freshly discovered account has a play rate but no aggregated scopes
        // yet. It must still score (a weighted mean, not a product) — just only
        // on the component we can actually measure.
        var result = DedicationScore.Compute(new DedicationInputs(
            PlayRate: 1d, CareerGames: 0, PatchSpan: 0, DaysSinceLastGame: double.PositiveInfinity));

        result.Score.Should().Be(Math.Round(100d * DedicationScore.CommitmentWeight, 1));
    }

    [Fact]
    public void Compute_rounds_the_score_to_one_decimal()
    {
        var result = DedicationScore.Compute(new DedicationInputs(
            PlayRate: 0.5713d, CareerGames: 137, PatchSpan: 3, DaysSinceLastGame: 4.4d));

        result.Score.Should().Be(Math.Round(result.Score, 1));
    }
}
