using AwesomeAssertions;
using Core.Lol.Performance;

namespace TrueMain.UnitTests;

public sealed class PerformanceScoreTests
{
    /// <summary>
    /// Reference stat line used as the base for most cases: a strong MIDDLE game
    /// with every component available except the mid game (the lead curve stops
    /// at 15, as it does for any game that ends before minute 20). Hand-computed
    /// expected score below.
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
        LaneLeads = new[] { new LaneLead(15, GoldDiff: 750, CsDiff: 15, XpDiff: 750) },
        OutOfLaneTakedowns = 2,
    };

    [Fact]
    public void Compute_matches_the_hand_computed_reference_vector()
    {
        // Golden vector — the whole point of the model being documented is that
        // a reader can reproduce it by hand. MIDDLE weights are combat 20 / kp 14 /
        // damage 18 / gold 7 / farm 14 / vision 5 / laning 10 / midgame 6 / roam 6.
        //
        //   combat  = (10 + 10) / 2 = 10 KDA, capped at 6 →              1.0000
        //   kp      = 20 / 30 →                                          0.6667
        //   damage  = 30% share, band 5%..35% → 0.25 / 0.30 →            0.8333
        //   gold    = 25% share, band 10%..30% → 0.15 / 0.20 →           0.7500
        //   farm    = 250 / 25 = 10 cs/min, ref 9.0, capped →            1.0000
        //   vision  = 25 / 25 = 1.0 /min, ref 0.9, capped →              1.0000
        //   laning  = one @15 mark; spans are 100g/2cs/100xp per minute, so
        //             ±1500 g, ±30 cs, ±1500 xp at that mark. +750/+15/+750
        //             → 0.75 on all three →                              0.7500
        //   midgame = no mark past 15 → dropped, weight redistributed
        //   roam    = 2 out-of-lane takedowns, MIDDLE ref 2.5 →          0.8000
        //
        //   weighted = 20 + 9.3333 + 15 + 5.25 + 14 + 5 + 7.5 + 4.8 = 80.8833
        //   over a surviving weight of 94 → 86.05 → 86
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
    public void Compute_drops_the_laning_component_when_no_mark_is_covered()
    {
        // Missing data must never be scored as a zero — the laning weight is
        // redistributed over the surviving components instead. For this
        // above-average line that means the score goes *up* (86 → 87), not down.
        //
        //   weighted without laning = 73.3833 over a total weight of 84 → 87.36 → 87
        var withoutLaning = Reference() with { LaneLeads = Array.Empty<LaneLead>() };

        PerformanceScore.Compute(withoutLaning).Should().Be(87);
    }

    [Fact]
    public void Compute_scores_an_even_lane_below_a_missing_lane_for_a_strong_player()
    {
        // A dead-even lane is worth 0.5 on a component where the rest of this
        // player's game sits well above 0.5, so it drags the average down —
        // whereas a missing snapshot leaves the average untouched. Both must
        // still beat what a hard-lost lane produces.
        var evenLane = Reference() with { LaneLeads = new[] { new LaneLead(15, 0, 0, 0) } };
        var lostLane = Reference() with { LaneLeads = new[] { new LaneLead(15, -2000, -40, -2000) } };
        var noLane = Reference() with { LaneLeads = Array.Empty<LaneLead>() };

        PerformanceScore.Compute(noLane).Should().BeGreaterThan(PerformanceScore.Compute(evenLane));
        PerformanceScore.Compute(evenLane).Should().BeGreaterThan(PerformanceScore.Compute(lostLane));
    }

    [Fact]
    public void Compute_grades_each_mark_against_a_span_proportional_to_its_minute()
    {
        // The saturation span is 100 gold / 2 cs / 100 xp per elapsed minute, so
        // half the span is the same *relative* lead at any mark and must grade
        // identically. Two marks, same relative lead → the same 0.75 the single
        // @15 reference produces, hence the same score.
        var proportional = Reference() with
        {
            LaneLeads = new[]
            {
                new LaneLead(5, GoldDiff: 250, CsDiff: 5, XpDiff: 250),
                new LaneLead(15, GoldDiff: 750, CsDiff: 15, XpDiff: 750),
            },
        };

        PerformanceScore.Compute(proportional).Should().Be(PerformanceScore.Compute(Reference()));
    }

    [Fact]
    public void Compute_rates_the_same_absolute_lead_higher_the_earlier_it_was_taken()
    {
        // 750 gold at 5 minutes is a dominant lane; the same 750 at 15 is a good
        // one. The proportional span is what makes the model say so.
        var earlyLead = Reference() with { LaneLeads = new[] { new LaneLead(5, 750, 15, 750) } };
        var lateLead = Reference() with { LaneLeads = new[] { new LaneLead(15, 750, 15, 750) } };

        PerformanceScore.Compute(earlyLead)
            .Should().BeGreaterThan(PerformanceScore.Compute(lateLead));
    }

    [Fact]
    public void Compute_weights_a_later_laning_mark_above_an_earlier_one()
    {
        // Within the laning component each mark carries its own minute as its
        // weight, so where the lane *ended up* counts more than where it started.
        var strongLate = Reference() with
        {
            LaneLeads = new[] { new LaneLead(5, -250, -5, -250), new LaneLead(15, 750, 15, 750) },
        };
        var strongEarly = Reference() with
        {
            LaneLeads = new[] { new LaneLead(5, 250, 5, 250), new LaneLead(15, -750, -15, -750) },
        };

        PerformanceScore.Compute(strongLate)
            .Should().BeGreaterThan(PerformanceScore.Compute(strongEarly));
    }

    [Fact]
    public void Compute_splits_the_lead_curve_into_a_laning_and_a_mid_game_component()
    {
        // Marks past minute 15 feed a component of their own, so a player who
        // won lane and then let it evaporate scores below one who kept extending.
        // The spans there are 2 000 g @20 and 3 000 g @30, so these leads are
        // saturating ones — a merely proportional lead would grade 0.75 and, on a
        // line already averaging 0.86, would pull the score *down*.
        var extended = Reference() with
        {
            LaneLeads = new[]
            {
                new LaneLead(15, 750, 15, 750),
                new LaneLead(20, 3_000, 60, 3_000),
                new LaneLead(30, 4_500, 90, 4_500),
            },
        };
        var evaporated = Reference() with
        {
            LaneLeads = new[]
            {
                new LaneLead(15, 750, 15, 750),
                new LaneLead(20, -3_000, -60, -3_000),
                new LaneLead(30, -4_500, -90, -4_500),
            },
        };
        var laneOnly = Reference();

        PerformanceScore.Compute(extended).Should().BeGreaterThan(PerformanceScore.Compute(laneOnly));
        PerformanceScore.Compute(laneOnly).Should().BeGreaterThan(PerformanceScore.Compute(evaporated));
    }

    [Fact]
    public void Compute_ignores_a_non_positive_mark_minute()
    {
        // A minute of 0 would divide the span by zero. Such a row is skipped, so
        // a curve made only of them grades as no curve at all.
        var bogus = Reference() with { LaneLeads = new[] { new LaneLead(0, 5000, 100, 5000) } };
        var noLane = Reference() with { LaneLeads = Array.Empty<LaneLead>() };

        PerformanceScore.Compute(bogus).Should().Be(PerformanceScore.Compute(noLane));
    }

    [Fact]
    public void Compute_distinguishes_an_uncovered_roam_from_a_zero_roam()
    {
        // null = "this match has no kill-position rows" → drop the component.
        // 0 = "the match is covered and the player never left lane" → grade it 0.
        var uncovered = Reference() with { OutOfLaneTakedowns = null };
        var neverRoamed = Reference() with { OutOfLaneTakedowns = 0 };

        PerformanceScore.Compute(uncovered)
            .Should().BeGreaterThan(PerformanceScore.Compute(neverRoamed));
    }

    [Fact]
    public void Compute_drops_the_roam_component_for_a_jungler()
    {
        // A jungler has no own lane, so every gank would read as a roam. The
        // JUNGLE profile zeroes the component, which makes the takedown count
        // irrelevant to the score instead of a free 100%.
        var homebody = new PerformanceScoreInput
        {
            TeamPosition = "JUNGLE",
            Kills = 5,
            Deaths = 4,
            Assists = 12,
            TeamKills = 30,
            DamageToChampions = 18_000,
            TeamDamageToChampions = 100_000,
            GoldEarned = 12_000,
            TeamGoldEarned = 60_000,
            Cs = 160,
            VisionScore = 30,
            GameDurationMinutes = 25d,
            OutOfLaneTakedowns = 0,
        };

        PerformanceScore.Compute(homebody with { OutOfLaneTakedowns = 12 })
            .Should().Be(PerformanceScore.Compute(homebody));
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
        var dominant = new[]
        {
            new LaneLead(5, 20_000, 200, 20_000),
            new LaneLead(10, 20_000, 200, 20_000),
            new LaneLead(15, 20_000, 200, 20_000),
            new LaneLead(20, 20_000, 200, 20_000),
            new LaneLead(30, 20_000, 200, 20_000),
        };

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
            LaneLeads = dominant,
            OutOfLaneTakedowns = 20,
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
            LaneLeads = dominant.Select(l => new LaneLead(l.Minute, -l.GoldDiff, -l.CsDiff, -l.XpDiff)).ToList(),
            OutOfLaneTakedowns = 0,
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
            LaneLeads = new[] { new LaneLead(15, GoldDiff: 150, CsDiff: 2, XpDiff: 100) },
            OutOfLaneTakedowns = 3,
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
        // Zero-length game, no team kills, no team damage or gold, no timeline:
        // every component except combat drops out, and combat alone still yields
        // a bounded score rather than a divide-by-zero.
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

    [Fact]
    public void Explain_returns_every_component_in_enum_order()
    {
        var breakdown = PerformanceScore.Explain(Reference());

        breakdown.Components.Select(c => c.Kind)
            .Should().Equal(Enum.GetValues<PerformanceComponentKind>());
    }

    [Fact]
    public void Explain_agrees_with_compute_and_reproduces_the_score_from_its_parts()
    {
        var breakdown = PerformanceScore.Explain(Reference());

        breakdown.Score.Should().Be(PerformanceScore.Compute(Reference()));

        // The published contract: sum(effectiveWeight × value) × 100 is the score.
        var rebuilt = breakdown.Components.Sum(c => c.EffectiveWeight * (c.Value ?? 0d)) * 100d;
        rebuilt.Should().BeApproximately(breakdown.Score, 0.5d);

        breakdown.Components.Sum(c => c.EffectiveWeight).Should().BeApproximately(1d, 1e-9);
    }

    [Fact]
    public void Explain_marks_a_dropped_component_null_and_gives_it_no_effective_weight()
    {
        var breakdown = PerformanceScore.Explain(Reference());

        var midGame = breakdown.Components.Single(c => c.Kind == PerformanceComponentKind.MidGame);
        midGame.Value.Should().BeNull();
        midGame.EffectiveWeight.Should().Be(0d);

        // The nominal weight is still reported — it is a property of the role,
        // not of this particular game.
        midGame.Weight.Should().BeGreaterThan(0d);
    }

    [Fact]
    public void Explain_reports_the_role_weights_of_the_position()
    {
        var support = PerformanceScore.Explain(Reference() with { TeamPosition = "UTILITY" });
        var mid = PerformanceScore.Explain(Reference());

        double WeightOf(PerformanceScoreBreakdown b, PerformanceComponentKind kind)
            => b.Components.Single(c => c.Kind == kind).Weight;

        WeightOf(support, PerformanceComponentKind.Vision)
            .Should().BeGreaterThan(WeightOf(mid, PerformanceComponentKind.Vision));
        WeightOf(support, PerformanceComponentKind.DamageShare)
            .Should().BeLessThan(WeightOf(mid, PerformanceComponentKind.DamageShare));
    }
}
