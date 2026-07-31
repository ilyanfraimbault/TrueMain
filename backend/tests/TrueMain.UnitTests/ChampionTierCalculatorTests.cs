using AwesomeAssertions;
using TrueMain.Options;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

public sealed class ChampionTierCalculatorTests
{
    private const string Lane = "MIDDLE";

    [Fact]
    public void Evaluate_returns_empty_for_no_inputs()
    {
        ChampionTierCalculator.Evaluate([], DefaultOptions()).Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_preserves_input_order()
    {
        var inputs = MakeInputs(20);

        var results = ChampionTierCalculator.Evaluate(inputs, DefaultOptions());

        results.Should().HaveCount(inputs.Count);

        // Guard the scatter-back: results come out indexed by the *original* input
        // position, not by sorted rank, so the input with the highest winRate must
        // score at least as well as the one with the lowest. A bug that mapped
        // results to sorted order instead would slip past the count check above.
        var maxWrIndex = inputs.IndexOf(inputs.MaxBy(input => (double)input.Wins / input.Games)!);
        var minWrIndex = inputs.IndexOf(inputs.MinBy(input => (double)input.Wins / input.Games)!);
        results[maxWrIndex].Score.Should().BeGreaterThanOrEqualTo(results[minWrIndex].Score);
    }

    [Fact]
    public void Evaluate_gives_a_single_row_the_top_tier()
    {
        var results = ChampionTierCalculator.Evaluate(
            [new ChampionTierCalculator.TierInput(Lane, Games: 100, Wins: 50, PickRate: 0.1, BanRate: 0.05)],
            DefaultOptions());

        results.Should().ContainSingle().Which.Tier.Should().Be(ChampionTierCalculator.TierS);
    }

    [Fact]
    public void Shrinkage_keeps_a_micro_sample_out_of_the_top_tier()
    {
        // The exact bug report (#971): a handful of games at a flattering raw win
        // rate must not fluke into S ahead of a well-played, average-winrate field.
        var inputs = new List<ChampionTierCalculator.TierInput>
        {
            new(Lane, Games: 12, Wins: 8, PickRate: 0.01, BanRate: 0.01), // micro sample, 66.7% raw WR
        };
        inputs.AddRange(Enumerable.Range(0, 9)
            .Select(_ => new ChampionTierCalculator.TierInput(Lane, Games: 600, Wins: 318, PickRate: 0.09, BanRate: 0.10)));

        var results = ChampionTierCalculator.Evaluate(inputs, DefaultOptions());

        results[0].Score.Should().BeLessThan(results[1].Score,
            "a 12-game 66.7% WR fluke must not outscore a 600-game 53% WR staple");
        results[0].Tier.Should().Be(ChampionTierCalculator.TierD,
            "the micro sample has the field's lowest pick rate, ban rate, and (post-shrinkage) win rate");
    }

    [Fact]
    public void Shrinkage_favours_the_larger_sample_at_equal_raw_winrate()
    {
        // 8 filler rows anchor the field's prior below 0.60. Two candidate rows
        // share the exact same *raw* 0.60 win rate and the exact same pick/ban
        // rate (so those terms contribute identically to both) — only sample
        // size differs, so only shrinkage can separate them.
        var inputs = new List<ChampionTierCalculator.TierInput>();
        for (var i = 0; i < 8; i++)
        {
            inputs.Add(new ChampionTierCalculator.TierInput(Lane, Games: 200, Wins: 100, PickRate: 0.05, BanRate: 0.05));
        }
        inputs.Add(new ChampionTierCalculator.TierInput(Lane, Games: 20, Wins: 12, PickRate: 0.05, BanRate: 0.05)); // index 8, small sample
        inputs.Add(new ChampionTierCalculator.TierInput(Lane, Games: 500, Wins: 300, PickRate: 0.05, BanRate: 0.05)); // index 9, large sample

        var results = ChampionTierCalculator.Evaluate(inputs, DefaultOptions());

        results[9].Score.Should().BeGreaterThan(results[8].Score,
            "the larger sample's raw 60% WR is shrunk less toward the field's ~52% prior");
    }

    [Fact]
    public void Pick_rate_outweighs_win_rate()
    {
        // Ban rate held equal between the two rows so it contributes identically
        // to both scores and cannot be the deciding factor.
        var highPickLowWin = new ChampionTierCalculator.TierInput(Lane, Games: 600, Wins: 318, PickRate: 0.20, BanRate: 0.10); // 53% WR
        var lowPickHighWin = new ChampionTierCalculator.TierInput(Lane, Games: 600, Wins: 450, PickRate: 0.02, BanRate: 0.10); // 75% WR

        var results = ChampionTierCalculator.Evaluate([highPickLowWin, lowPickHighWin], DefaultOptions());

        results[0].Score.Should().BeGreaterThan(results[1].Score,
            "the busiest pick with a merely-average win rate must outscore a rarely-played pick with a high win rate");
    }

    [Fact]
    public void Ban_rate_outweighs_win_rate()
    {
        // Pick rate held equal between the two rows so it contributes identically
        // to both scores and cannot be the deciding factor.
        var highBanLowWin = new ChampionTierCalculator.TierInput(Lane, Games: 600, Wins: 318, PickRate: 0.05, BanRate: 0.30); // 53% WR
        var lowBanHighWin = new ChampionTierCalculator.TierInput(Lane, Games: 600, Wins: 450, PickRate: 0.05, BanRate: 0.02); // 75% WR

        var results = ChampionTierCalculator.Evaluate([highBanLowWin, lowBanHighWin], DefaultOptions());

        results[0].Score.Should().BeGreaterThan(results[1].Score,
            "the most-banned row with a merely-average win rate must outscore a rarely-banned row with a high win rate");
    }

    [Fact]
    public void Pick_rate_outweighs_ban_rate()
    {
        // Identical games/wins on both rows ties their (post-shrinkage) win
        // percentile exactly, isolating the pick-vs-ban comparison.
        var highPickLowBan = new ChampionTierCalculator.TierInput(Lane, Games: 600, Wins: 318, PickRate: 0.20, BanRate: 0.02);
        var lowPickHighBan = new ChampionTierCalculator.TierInput(Lane, Games: 600, Wins: 318, PickRate: 0.02, BanRate: 0.20);

        var results = ChampionTierCalculator.Evaluate([highPickLowBan, lowPickHighBan], DefaultOptions());

        results[0].Score.Should().BeGreaterThan(results[1].Score,
            "pick rate carries more weight than ban rate");
    }

    [Fact]
    public void Null_ban_rate_renormalizes_weights_to_sum_to_one()
    {
        // The field's top row (best pick rate, best win rate) should score
        // exactly 1.0 when ban data is absent — proof the ban weight was folded
        // back into pick + win (which then sum to 1.0) rather than merely
        // dropped (which would cap the best possible score at 0.70).
        var top = new ChampionTierCalculator.TierInput(Lane, Games: 600, Wins: 450, PickRate: 0.20, BanRate: null);
        var bottom = new ChampionTierCalculator.TierInput(Lane, Games: 600, Wins: 200, PickRate: 0.01, BanRate: null);

        var results = ChampionTierCalculator.Evaluate([top, bottom], DefaultOptions());

        results[0].Score.Should().BeApproximately(1.0, 1e-9);
        results[1].Score.Should().BeApproximately(0.0, 1e-9);
    }

    [Fact]
    public void A_single_missing_ban_rate_disables_the_ban_term_for_the_whole_call()
    {
        // Ban data is populated per-patch, not per-champion, so a mix of
        // null/non-null within one call is scored the same as fully-null
        // (all-or-nothing) rather than defaulting the missing row to 0%.
        var top = new ChampionTierCalculator.TierInput(Lane, Games: 600, Wins: 450, PickRate: 0.20, BanRate: null);
        var other = new ChampionTierCalculator.TierInput(Lane, Games: 600, Wins: 200, PickRate: 0.01, BanRate: 0.10);

        var results = ChampionTierCalculator.Evaluate([top, other], DefaultOptions());

        results[0].Score.Should().BeApproximately(1.0, 1e-9,
            "one null ban rate in the field disables ban scoring entirely, so the top pick+win row still reaches the full 1.0");
    }

    [Fact]
    public void Pick_rate_is_ranked_within_its_own_lane_not_patch_wide()
    {
        // MIDDLE has more playable champions than UTILITY, so a support's raw
        // pick share is mechanically higher for the same "how central is this
        // pick to its role" meaning. Two rows below are each the exact median
        // of their own lane despite very different raw pick rates (0.06 vs
        // 0.12) — per-lane normalization must score them identically; a
        // patch-wide min-max/percentile normalization would not.
        var middleLane = new[]
        {
            new ChampionTierCalculator.TierInput("MIDDLE", Games: 600, Wins: 318, PickRate: 0.02, BanRate: 0.10),
            new ChampionTierCalculator.TierInput("MIDDLE", Games: 600, Wins: 318, PickRate: 0.06, BanRate: 0.10), // median
            new ChampionTierCalculator.TierInput("MIDDLE", Games: 600, Wins: 318, PickRate: 0.09, BanRate: 0.10),
        };
        var utilityLane = new[]
        {
            new ChampionTierCalculator.TierInput("UTILITY", Games: 600, Wins: 318, PickRate: 0.05, BanRate: 0.10),
            new ChampionTierCalculator.TierInput("UTILITY", Games: 600, Wins: 318, PickRate: 0.12, BanRate: 0.10), // median
            new ChampionTierCalculator.TierInput("UTILITY", Games: 600, Wins: 318, PickRate: 0.20, BanRate: 0.10),
        };

        var inputs = middleLane.Concat(utilityLane).ToList();
        var results = ChampionTierCalculator.Evaluate(inputs, DefaultOptions());

        results[1].Score.Should().BeApproximately(results[4].Score, 1e-9,
            "both rows are the median pick rate of their own lane, so per-lane percentile ranking scores them equally " +
            "despite their raw pick rates (0.06 vs 0.12) differing by 2x");
    }

    [Fact]
    public void Evaluate_produces_a_pyramid_across_a_full_lane()
    {
        // 100 rows on one lane, win rate climbing, pick/ban rate held constant
        // (so those two terms contribute equally to every row and only win
        // rate differentiates) — bucket sizes should match the documented
        // 10/20/35/25/10 split exactly.
        var inputs = Enumerable.Range(0, 100)
            .Select(i => new ChampionTierCalculator.TierInput(
                Lane, Games: 300, Wins: (int)Math.Round(300 * (0.40 + (i * 0.002))), PickRate: 0.05, BanRate: 0.10))
            .ToList();

        var results = ChampionTierCalculator.Evaluate(inputs, DefaultOptions());

        var counts = results
            .GroupBy(result => result.Tier)
            .ToDictionary(group => group.Key, group => group.Count());

        counts[ChampionTierCalculator.TierS].Should().Be(10);
        counts[ChampionTierCalculator.TierA].Should().Be(20);
        counts[ChampionTierCalculator.TierB].Should().Be(35);
        counts[ChampionTierCalculator.TierC].Should().Be(25);
        counts[ChampionTierCalculator.TierD].Should().Be(10);

        // The strongest raw win rate must still head the pyramid: pick/ban are
        // tied for every row here, so win rate is the sole discriminator.
        results[99].Tier.Should().Be(ChampionTierCalculator.TierS);
        results[0].Tier.Should().Be(ChampionTierCalculator.TierD);
    }

    [Fact]
    public void Evaluate_stays_finite_when_every_row_has_zero_games()
    {
        var inputs = new List<ChampionTierCalculator.TierInput>
        {
            new(Lane, Games: 0, Wins: 0, PickRate: 0.1, BanRate: 0.1),
            new(Lane, Games: 0, Wins: 0, PickRate: 0.2, BanRate: 0.2),
        };

        var results = ChampionTierCalculator.Evaluate(inputs, DefaultOptions());

        results.Should().OnlyContain(result => double.IsFinite(result.Score));
        results.Should().OnlyContain(result => ValidTiers.Contains(result.Tier));
    }

    private static readonly string[] ValidTiers =
    [
        ChampionTierCalculator.TierS,
        ChampionTierCalculator.TierA,
        ChampionTierCalculator.TierB,
        ChampionTierCalculator.TierC,
        ChampionTierCalculator.TierD,
    ];

    private static ChampionTierOptions DefaultOptions() => new();

    private static List<ChampionTierCalculator.TierInput> MakeInputs(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new ChampionTierCalculator.TierInput(
                Lane,
                Games: 100,
                Wins: 45 + (i % 10),
                PickRate: 0.02 + ((i % 5) * 0.01),
                BanRate: 0.05 + ((i % 3) * 0.02)))
            .ToList();
}
