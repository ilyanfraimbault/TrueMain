using AwesomeAssertions;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

public sealed class ChampionTierCalculatorTests
{
    [Fact]
    public void Assign_returns_empty_for_no_inputs()
    {
        ChampionTierCalculator.Assign([]).Should().BeEmpty();
    }

    [Fact]
    public void Assign_preserves_input_order()
    {
        var inputs = MakeInputs(20);

        var tiers = ChampionTierCalculator.Assign(inputs);

        tiers.Should().HaveCount(inputs.Count);
    }

    [Fact]
    public void Assign_gives_a_single_row_the_top_tier()
    {
        var tiers = ChampionTierCalculator.Assign(
            [new ChampionTierCalculator.TierInput(WinRate: 0.5, PickRate: 0.1)]);

        tiers.Should().ContainSingle().Which.Should().Be(ChampionTierCalculator.TierS);
    }

    [Fact]
    public void Assign_ranks_a_higher_winrate_into_a_better_or_equal_tier()
    {
        // Hold pickRate constant so winRate is the only differentiator, then
        // confirm the ranking is monotonic: a strictly higher winRate never
        // lands in a strictly worse tier than a lower one.
        var inputs = Enumerable.Range(0, 50)
            // i = 0 is the weakest (0.40 WR), i = 49 the strongest (0.65 WR).
            .Select(i => new ChampionTierCalculator.TierInput(
                WinRate: 0.40 + (i * 0.005),
                PickRate: 0.05))
            .ToList();

        var tiers = ChampionTierCalculator.Assign(inputs);

        // The strongest row must outrank (or tie) the weakest one.
        TierRank(tiers[49]).Should().BeLessThanOrEqualTo(TierRank(tiers[0]));
        tiers[49].Should().Be(ChampionTierCalculator.TierS, "the top winRate with constant pickRate heads the patch");
        tiers[0].Should().Be(ChampionTierCalculator.TierD, "the bottom winRate with constant pickRate trails the patch");
    }

    [Fact]
    public void Assign_rewards_meta_presence_when_winrates_tie()
    {
        // Two rows share the exact same winRate; the one with the far larger
        // pickRate should score higher and therefore tier at least as well.
        var inputs = new List<ChampionTierCalculator.TierInput>
        {
            new(WinRate: 0.52, PickRate: 0.01), // niche
            new(WinRate: 0.52, PickRate: 0.30), // staple
        };

        var tiers = ChampionTierCalculator.Assign(inputs);

        TierRank(tiers[1]).Should().BeLessThanOrEqualTo(TierRank(tiers[0]),
            "equal winRate but higher pickRate must not tier worse");
    }

    [Fact]
    public void Assign_falls_back_to_winrate_only_when_every_pickrate_is_zero()
    {
        // Degenerate patch: no pickRate signal at all. The normalized term
        // collapses to 0 and the ranking must stay a valid winRate ordering
        // (no divide-by-zero, no NaN tiers).
        var inputs = new List<ChampionTierCalculator.TierInput>
        {
            new(WinRate: 0.60, PickRate: 0.0),
            new(WinRate: 0.45, PickRate: 0.0),
        };

        var tiers = ChampionTierCalculator.Assign(inputs);

        tiers.Should().OnlyContain(tier => ValidTiers.Contains(tier));
        TierRank(tiers[0]).Should().BeLessThanOrEqualTo(TierRank(tiers[1]));
    }

    [Fact]
    public void Assign_produces_a_pyramid_across_a_full_patch()
    {
        // A 100-row patch lines up 1:1 with the percentile bands, so the bucket
        // sizes should match the documented 10/20/35/25/10 split exactly.
        var inputs = Enumerable.Range(0, 100)
            .Select(i => new ChampionTierCalculator.TierInput(
                WinRate: 0.40 + (i * 0.002),
                PickRate: 0.05))
            .ToList();

        var tiers = ChampionTierCalculator.Assign(inputs);

        var counts = tiers
            .GroupBy(tier => tier)
            .ToDictionary(group => group.Key, group => group.Count());

        counts[ChampionTierCalculator.TierS].Should().Be(10);
        counts[ChampionTierCalculator.TierA].Should().Be(20);
        counts[ChampionTierCalculator.TierB].Should().Be(35);
        counts[ChampionTierCalculator.TierC].Should().Be(25);
        counts[ChampionTierCalculator.TierD].Should().Be(10);
    }

    private static readonly string[] ValidTiers =
    [
        ChampionTierCalculator.TierS,
        ChampionTierCalculator.TierA,
        ChampionTierCalculator.TierB,
        ChampionTierCalculator.TierC,
        ChampionTierCalculator.TierD,
    ];

    // Lower rank = stronger tier, so S < A < B < C < D for comparisons.
    private static int TierRank(string tier) => Array.IndexOf(ValidTiers, tier);

    private static List<ChampionTierCalculator.TierInput> MakeInputs(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new ChampionTierCalculator.TierInput(
                WinRate: 0.45 + ((i % 10) * 0.01),
                PickRate: 0.02 + ((i % 5) * 0.01)))
            .ToList();
}
