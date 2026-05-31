using AwesomeAssertions;
using Core.Lol.Ranking;

namespace TrueMain.UnitTests;

public sealed class RankScoreTests
{
    [Theory]
    [InlineData("IRON", "IV", 0, 0)]
    [InlineData("IRON", "I", 0, 300)]
    [InlineData("DIAMOND", "IV", 0, 2400)]
    [InlineData("DIAMOND", "I", 99, 2799)]
    [InlineData("MASTER", "I", 0, 2800)]
    [InlineData("MASTER", "I", 20, 2820)]
    [InlineData("MASTER", "I", 2625, 5425)]
    [InlineData("GRANDMASTER", "I", 800, 3600)]
    [InlineData("CHALLENGER", "I", 1389, 4189)]
    public void Compute_returns_expected_score(string tier, string division, int lp, int expected)
    {
        RankScore.Compute(tier, division, lp).Should().Be(expected);
    }

    [Fact]
    public void Compute_orders_Master_above_DiamondI()
    {
        // Acceptance criterion from the leaderboard spec:
        // a Master 20 LP must sit above a Diamond I 90 LP.
        var diamond = RankScore.Compute("DIAMOND", "I", 90);
        var master = RankScore.Compute("MASTER", "I", 20);

        master.Should().BeGreaterThan(diamond!.Value);
    }

    [Fact]
    public void Compute_orders_high_LP_Master_above_low_LP_Challenger()
    {
        // The screenshot ladder lists Master 2625 LP above Challenger 800 LP
        // because LP at the apex is unbounded and ladders break ties on raw
        // LP, not on the apex tier name.
        var master = RankScore.Compute("MASTER", "I", 2625);
        var challenger = RankScore.Compute("CHALLENGER", "I", 800);

        master.Should().BeGreaterThan(challenger!.Value);
    }

    [Fact]
    public void Compute_is_case_insensitive()
    {
        var canonical = RankScore.Compute("DIAMOND", "II", 50);
        var lower = RankScore.Compute("diamond", "ii", 50);
        var mixed = RankScore.Compute("Diamond", "Ii", 50);

        lower.Should().Be(canonical);
        mixed.Should().Be(canonical);
    }

    [Fact]
    public void Compute_trims_whitespace()
    {
        var canonical = RankScore.Compute("PLATINUM", "IV", 75);
        var padded = RankScore.Compute("  PLATINUM  ", "  IV  ", 75);

        padded.Should().Be(canonical);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("UNRANKED")]
    [InlineData("BRONZ")]
    public void Compute_returns_null_for_missing_or_unknown_tier(string? tier)
    {
        RankScore.Compute(tier, "I", 50).Should().BeNull();
    }

    [Fact]
    public void Compute_treats_apex_division_as_zero_even_if_blank()
    {
        // Riot returns Division="I" for Master/GM/Challenger but ingestor
        // history has a few rows where the column was blank. Either way the
        // apex tiers should compute the same score.
        var withDivision = RankScore.Compute("MASTER", "I", 50);
        var blankDivision = RankScore.Compute("MASTER", "", 50);
        var nullDivision = RankScore.Compute("MASTER", null, 50);

        withDivision.Should().Be(blankDivision);
        withDivision.Should().Be(nullDivision);
    }
}
