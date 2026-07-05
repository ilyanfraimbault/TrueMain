using Core.Lol.Ranking;
using AwesomeAssertions;

namespace TrueMain.UnitTests;

public sealed class EloBracketTests
{
    [Theory]
    [InlineData("IRON", EloBracket.IronGold)]
    [InlineData("BRONZE", EloBracket.IronGold)]
    [InlineData("SILVER", EloBracket.IronGold)]
    [InlineData("GOLD", EloBracket.IronGold)]
    [InlineData("PLATINUM", EloBracket.PlatinumEmerald)]
    [InlineData("EMERALD", EloBracket.PlatinumEmerald)]
    [InlineData("DIAMOND", EloBracket.DiamondPlus)]
    [InlineData("MASTER", EloBracket.MasterPlus)]
    [InlineData("GRANDMASTER", EloBracket.MasterPlus)]
    [InlineData("CHALLENGER", EloBracket.MasterPlus)]
    public void FromTier_MapsKnownRiotTiersToTheirBracket(string tier, string expected)
    {
        EloBracket.FromTier(tier).Should().Be(expected);
    }

    [Theory]
    [InlineData("diamond")]
    [InlineData(" Diamond ")]
    [InlineData("DiAmOnD")]
    public void FromTier_IsCaseAndWhitespaceInsensitive(string tier)
    {
        EloBracket.FromTier(tier).Should().Be(EloBracket.DiamondPlus);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("UNRANKED")]
    [InlineData("not-a-tier")]
    public void FromTier_FallsBackToUnrankedForUnknownOrMissingTiers(string? tier)
    {
        EloBracket.FromTier(tier).Should().Be(EloBracket.Unranked);
    }

    [Theory]
    [InlineData("ALL", EloBracket.All)]
    [InlineData("iron_gold", EloBracket.IronGold)]
    [InlineData(" master_plus ", EloBracket.MasterPlus)]
    [InlineData("UNRANKED", EloBracket.Unranked)]
    public void Normalize_CanonicalisesRecognisedBrackets(string raw, string expected)
    {
        EloBracket.Normalize(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("platinum")] // a tier name, not a bracket key
    [InlineData("garbage")]
    public void Normalize_ReturnsNullForBlankOrUnrecognisedInput(string? raw)
    {
        EloBracket.Normalize(raw).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ALL")]
    [InlineData("all")]
    public void IsAll_TreatsBlankAndAllAsEveryGame(string? bracket)
    {
        EloBracket.IsAll(bracket).Should().BeTrue();
    }

    [Theory]
    [InlineData(EloBracket.IronGold)]
    [InlineData(EloBracket.MasterPlus)]
    [InlineData(EloBracket.Unranked)]
    public void IsAll_IsFalseForASpecificBracket(string bracket)
    {
        EloBracket.IsAll(bracket).Should().BeFalse();
    }

    [Fact]
    public void Persisted_ExcludesTheSyntheticAllUnion()
    {
        EloBracket.Persisted.Should().NotContain(EloBracket.All);
        EloBracket.Persisted.Should().BeEquivalentTo(
        [
            EloBracket.IronGold,
            EloBracket.PlatinumEmerald,
            EloBracket.DiamondPlus,
            EloBracket.MasterPlus,
            EloBracket.Unranked
        ]);
    }

    [Fact]
    public void Selectable_LeadsWithAllAndOmitsUnranked()
    {
        EloBracket.Selectable[0].Should().Be(EloBracket.All);
        EloBracket.Selectable.Should().NotContain(EloBracket.Unranked);
    }
}
