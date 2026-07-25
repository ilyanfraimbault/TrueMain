using AwesomeAssertions;
using Core.Lol.Performance;

namespace TrueMain.UnitTests;

public sealed class PerformanceScoreTests
{
    /// <summary>
    /// Reference stat line used as the base for most cases: a strong MIDDLE game
    /// with every component available. Hand-computed expected score below.
    /// </summary>
    private static PerformanceScoreInput Reference() => new()
    {
        TeamPosition = "MIDDLE",
        Kills = 10,
        Deaths = 2,
        Assists = 10,
        TeamKills = 30,
        DamageToChampions = 30_000,
        TeamDamageToChampions = 100_000,
        GoldEarned = 15_000,
        TeamGoldEarned = 60_000,
        Cs = 250,
        VisionScore = 25,
        GameDurationMinutes = 25d,
        CsDiff15 = 15,
        GoldDiff15 = 750,
        XpDiff15 = 750,
    };

    [Fact]
    public void Compute_matches_the_hand_computed_reference_vector()
    {
        // Golden vector — the whole point of the model being documented is that
        // a reader can reproduce it by hand. MIDDLE weights are
        // combat 22 / kp 16 / damage 20 / gold 8 / farm 16 / vision 6 / laning 12.
        //
        //   combat  = (10 + 10) / 2 = 10 KDA, capped at 6 →              1.0000
        //   kp      = 20 / 30 →                                          0.6667
        //   damage  = 30% share, band 5%..35% → 0.25 / 0.30 →            0.8333
        //   gold    = 25% share, band 10%..30% → 0.15 / 0.20 →           0.7500
        //   farm    = 250 / 25 = 10 cs/min, ref 9.0, capped →            1.0000
        //   vision  = 25 / 25 = 1.0 /min, ref 0.9, capped →              1.0000
        //   laning  = +750g/+15cs/+750xp → 0.75 on all three →           0.7500
        //
        //   22 + 10.6667 + 16.6667 + 6 + 16 + 6 + 9 = 86.3333 → 86
        PerformanceScore.Compute(Reference()).Should().Be(86);
    }

    [Fact]
    public void Compute_is_deterministic_across_calls()
    {
        var first = PerformanceScore.Compute(Reference());
        var second = PerformanceScore.Compute(Reference());

        second.Should().Be(first);
    }

    [Fact]
    public void Compute_drops_the_laning_component_when_the_at15_snapshot_is_missing()
    {
        // Missing data must never be scored as a zero — the laning weight is
        // redistributed over the surviving components instead. For this
        // above-average line that means the score goes *up* (86 → 88), not down.
        //
        //   weighted without laning = 77.3333 over a total weight of 88 → 87.88 → 88
        var withoutLaning = Reference() with { CsDiff15 = null, GoldDiff15 = null, XpDiff15 = null };

        PerformanceScore.Compute(withoutLaning).Should().Be(88);
    }

    [Fact]
    public void Compute_scores_an_even_lane_below_a_missing_lane_for_a_strong_player()
    {
        // A dead-even lane is worth 0.5 on a component where the rest of this
        // player's game sits well above 0.5, so it drags the average down —
        // whereas a missing snapshot leaves the average untouched. Both must
        // still beat what a hard-lost lane produces.
        var evenLane = Reference() with { CsDiff15 = 0, GoldDiff15 = 0, XpDiff15 = 0 };
        var lostLane = Reference() with { CsDiff15 = -40, GoldDiff15 = -2000, XpDiff15 = -2000 };
        var noLane = Reference() with { CsDiff15 = null, GoldDiff15 = null, XpDiff15 = null };

        PerformanceScore.Compute(noLane).Should().BeGreaterThan(PerformanceScore.Compute(evenLane));
        PerformanceScore.Compute(evenLane).Should().BeGreaterThan(PerformanceScore.Compute(lostLane));
    }

    [Fact]
    public void Compute_is_monotonic_in_deaths()
    {
        var clean = Reference() with { Deaths = 1 };
        var feeding = Reference() with { Deaths = 12 };

        PerformanceScore.Compute(clean).Should().BeGreaterThan(PerformanceScore.Compute(feeding));
    }

    [Theory]
    [InlineData("TOP")]
    [InlineData("JUNGLE")]
    [InlineData("MIDDLE")]
    [InlineData("BOTTOM")]
    [InlineData("UTILITY")]
    [InlineData("")]
    [InlineData("SOMETHING_RIOT_INVENTED")]
    public void Compute_stays_inside_the_0_to_100_range(string position)
    {
        var perfect = new PerformanceScoreInput
        {
            TeamPosition = position,
            Kills = 40,
            Deaths = 0,
            Assists = 40,
            TeamKills = 40,
            DamageToChampions = 200_000,
            TeamDamageToChampions = 200_000,
            GoldEarned = 40_000,
            TeamGoldEarned = 40_000,
            Cs = 900,
            VisionScore = 200,
            GameDurationMinutes = 30d,
            CsDiff15 = 200,
            GoldDiff15 = 20_000,
            XpDiff15 = 20_000,
        };

        var hopeless = new PerformanceScoreInput
        {
            TeamPosition = position,
            Kills = 0,
            Deaths = 20,
            Assists = 0,
            TeamKills = 30,
            DamageToChampions = 0,
            TeamDamageToChampions = 100_000,
            GoldEarned = 3_000,
            TeamGoldEarned = 60_000,
            Cs = 0,
            VisionScore = 0,
            GameDurationMinutes = 30d,
            CsDiff15 = -200,
            GoldDiff15 = -20_000,
            XpDiff15 = -20_000,
        };

        PerformanceScore.Compute(perfect).Should().Be(100);
        PerformanceScore.Compute(hopeless).Should().Be(0);
    }

    [Fact]
    public void Compute_grades_a_support_stat_line_on_the_support_profile()
    {
        // 1.6 cs/min and 2.5 vision/min is an excellent support game and a
        // dreadful mid one. Same numbers, different role → different score.
        var support = new PerformanceScoreInput
        {
            TeamPosition = "UTILITY",
            Kills = 1,
            Deaths = 5,
            Assists = 22,
            TeamKills = 30,
            DamageToChampions = 9_000,
            TeamDamageToChampions = 100_000,
            GoldEarned = 8_500,
            TeamGoldEarned = 60_000,
            Cs = 40,
            VisionScore = 63,
            GameDurationMinutes = 25d,
            CsDiff15 = 2,
            GoldDiff15 = 150,
            XpDiff15 = 100,
        };

        var sameLineAsMid = support with { TeamPosition = "MIDDLE" };

        PerformanceScore.Compute(support)
            .Should().BeGreaterThan(PerformanceScore.Compute(sameLineAsMid));
    }

    [Fact]
    public void Compute_falls_back_to_the_neutral_profile_for_an_unknown_position()
    {
        var blank = Reference() with { TeamPosition = string.Empty };
        var garbage = Reference() with { TeamPosition = "NONE" };

        PerformanceScore.Compute(garbage).Should().Be(PerformanceScore.Compute(blank));
    }

    [Fact]
    public void Compute_is_case_and_whitespace_insensitive_on_the_position()
    {
        var padded = Reference() with { TeamPosition = "  middle  " };

        PerformanceScore.Compute(padded).Should().Be(PerformanceScore.Compute(Reference()));
    }

    [Fact]
    public void Compute_survives_a_remake_shaped_input_with_no_usable_denominators()
    {
        // Zero-length game, no team kills, no team damage or gold: every
        // component except combat drops out, and combat alone still yields a
        // bounded score rather than a divide-by-zero.
        var remake = new PerformanceScoreInput
        {
            TeamPosition = "TOP",
            Kills = 0,
            Deaths = 0,
            Assists = 0,
            TeamKills = 0,
            DamageToChampions = 0,
            TeamDamageToChampions = 0,
            GoldEarned = 500,
            TeamGoldEarned = 0,
            Cs = 0,
            VisionScore = 0,
            GameDurationMinutes = 0d,
        };

        PerformanceScore.Compute(remake).Should().Be(0);
    }

    [Fact]
    public void Compute_clamps_kill_participation_above_one()
    {
        // Shared assists let (kills + assists) exceed the team's kill total.
        // The component must clamp to 1 rather than inflating the average.
        var inflated = Reference() with { Kills = 10, Assists = 40, TeamKills = 20 };
        var exactlyFull = Reference() with { Kills = 10, Assists = 10, TeamKills = 20 };

        PerformanceScore.Compute(inflated)
            .Should().Be(PerformanceScore.Compute(exactlyFull));
    }

    [Fact]
    public void Compute_rejects_a_null_input()
    {
        var act = () => PerformanceScore.Compute(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
