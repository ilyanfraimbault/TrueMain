using AwesomeAssertions;
using Core.Lol.Ranking;

namespace TrueMain.UnitTests;

public sealed class EloBracketTests
{
    [Theory]
    [InlineData("IRON", EloBracket.Iron)]
    [InlineData("bronze", EloBracket.Bronze)]
    [InlineData("  Gold ", EloBracket.Gold)]
    [InlineData("PLATINUM", EloBracket.Platinum)]
    [InlineData("EMERALD", EloBracket.Emerald)]
    [InlineData("DIAMOND", EloBracket.Diamond)]
    [InlineData("MASTER", EloBracket.MasterPlus)]
    [InlineData("GRANDMASTER", EloBracket.MasterPlus)]
    [InlineData("CHALLENGER", EloBracket.MasterPlus)]
    [InlineData("", EloBracket.Unranked)]
    [InlineData(null, EloBracket.Unranked)]
    [InlineData("WOOD", EloBracket.Unranked)]
    public void FromTier_maps_to_per_tier_band(string? tier, string expected)
    {
        EloBracket.FromTier(tier).Should().Be(expected);
    }

    [Fact]
    public void BandsAtOrAbove_expands_a_threshold_cumulatively()
    {
        EloBracket.BandsAtOrAbove(EloBracket.GoldPlus)
            .Should().Equal(
                EloBracket.Gold,
                EloBracket.Platinum,
                EloBracket.Emerald,
                EloBracket.Diamond,
                EloBracket.MasterPlus);
    }

    [Fact]
    public void BandsAtOrAbove_master_plus_is_the_apex_band_only()
    {
        EloBracket.BandsAtOrAbove(EloBracket.MasterPlus)
            .Should().Equal(EloBracket.MasterPlus);
    }

    [Fact]
    public void BandsAtOrAbove_iron_plus_spans_every_ranked_band_but_not_unranked()
    {
        var bands = EloBracket.BandsAtOrAbove(EloBracket.IronPlus);

        bands.Should().Equal(
            EloBracket.Iron,
            EloBracket.Bronze,
            EloBracket.Silver,
            EloBracket.Gold,
            EloBracket.Platinum,
            EloBracket.Emerald,
            EloBracket.Diamond,
            EloBracket.MasterPlus);
        bands.Should().NotContain(EloBracket.Unranked);
    }

    [Theory]
    [InlineData(EloBracket.All)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public void BandsAtOrAbove_returns_null_for_all_or_unrecognised(string? threshold)
    {
        // Null tells callers "no elo clause" — the full union across bands.
        EloBracket.BandsAtOrAbove(threshold).Should().BeNull();
    }

    [Theory]
    [InlineData("gold_plus", EloBracket.GoldPlus)]
    [InlineData("  MASTER_PLUS  ", EloBracket.MasterPlus)]
    [InlineData("all", EloBracket.All)]
    [InlineData("gold", EloBracket.Gold)] // exact single tier
    [InlineData("DIAMOND", EloBracket.Diamond)]
    public void Normalize_canonicalises_known_values(string raw, string expected)
    {
        EloBracket.Normalize(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("IRON_GOLD")] // an old coarse-bucket value is neither threshold nor band
    [InlineData("nonsense")]
    public void Normalize_returns_null_for_blank_or_unknown(string? raw)
    {
        EloBracket.Normalize(raw).Should().BeNull();
    }

    [Fact]
    public void ResolveBands_exact_tier_selects_only_that_band()
    {
        EloBracket.ResolveBands(EloBracket.Gold).Should().Equal(EloBracket.Gold);
    }

    [Fact]
    public void ResolveBands_cumulative_threshold_expands()
    {
        EloBracket.ResolveBands(EloBracket.GoldPlus)
            .Should().Equal(
                EloBracket.Gold,
                EloBracket.Platinum,
                EloBracket.Emerald,
                EloBracket.Diamond,
                EloBracket.MasterPlus);
    }

    [Theory]
    [InlineData(EloBracket.All)]
    [InlineData(null)]
    [InlineData("garbage")]
    public void ResolveBands_returns_null_for_all_or_unrecognised(string? value)
    {
        EloBracket.ResolveBands(value).Should().BeNull();
    }

    [Fact]
    public void Selectable_interleaves_cumulative_then_exact_highest_to_lowest()
    {
        EloBracket.Selectable.Should().Equal(
            EloBracket.All,
            EloBracket.MasterPlus,
            EloBracket.DiamondPlus, EloBracket.Diamond,
            EloBracket.EmeraldPlus, EloBracket.Emerald,
            EloBracket.PlatinumPlus, EloBracket.Platinum,
            EloBracket.GoldPlus, EloBracket.Gold,
            EloBracket.SilverPlus, EloBracket.Silver,
            EloBracket.BronzePlus, EloBracket.Bronze,
            EloBracket.IronPlus, EloBracket.Iron);
    }
}
