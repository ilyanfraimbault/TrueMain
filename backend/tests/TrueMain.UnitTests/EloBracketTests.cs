using Core.Lol.Ranking;
using AwesomeAssertions;

namespace TrueMain.UnitTests;

public sealed class EloBracketTests
{
    [Theory]
    [InlineData("IRON", EloBracket.Iron)]
    [InlineData("BRONZE", EloBracket.Bronze)]
    [InlineData("SILVER", EloBracket.Silver)]
    [InlineData("GOLD", EloBracket.Gold)]
    [InlineData("PLATINUM", EloBracket.Platinum)]
    [InlineData("EMERALD", EloBracket.Emerald)]
    [InlineData("DIAMOND", EloBracket.Diamond)]
    public void FromTier_MapsEachRiotTierToItsOwnBucket(string tier, string expected)
    {
        EloBracket.FromTier(tier).Should().Be(expected);
    }

    [Theory]
    [InlineData("MASTER")]
    [InlineData("GRANDMASTER")]
    [InlineData("CHALLENGER")]
    public void FromTier_FoldsTheApexTiersIntoMaster(string tier)
    {
        EloBracket.FromTier(tier).Should().Be(EloBracket.Master);
    }

    [Theory]
    [InlineData("gold")]
    [InlineData(" Gold ")]
    [InlineData("GoLd")]
    public void FromTier_IsCaseAndWhitespaceInsensitive(string tier)
    {
        EloBracket.FromTier(tier).Should().Be(EloBracket.Gold);
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
    [InlineData("gold", EloBracket.Gold)]
    [InlineData(" gold_plus ", "GOLD_PLUS")]
    [InlineData("DIAMOND_PLUS", "DIAMOND_PLUS")]
    public void Normalize_CanonicalisesRecognisedFilters(string raw, string expected)
    {
        EloBracket.Normalize(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("UNRANKED")]      // stored bucket, but not a selectable filter
    [InlineData("UNRANKED_PLUS")]
    [InlineData("garbage")]
    public void Normalize_ReturnsNullForBlankOrUnrecognisedInput(string? raw)
    {
        EloBracket.Normalize(raw).Should().BeNull();
    }

    [Fact]
    public void ResolveFilter_BareTier_YieldsThatTierOnly()
    {
        EloBracket.ResolveFilter("GOLD").Should().Equal(EloBracket.Gold);
    }

    [Fact]
    public void ResolveFilter_TierPlus_YieldsThatTierAndEveryTierAbove()
    {
        EloBracket.ResolveFilter("GOLD_PLUS").Should().Equal(
            EloBracket.Gold,
            EloBracket.Platinum,
            EloBracket.Emerald,
            EloBracket.Diamond,
            EloBracket.Master);
    }

    [Fact]
    public void ResolveFilter_MasterPlus_CollapsesToMasterAlone()
    {
        // Master is the top of the ladder, so "and above" adds nothing.
        EloBracket.ResolveFilter("MASTER_PLUS").Should().Equal(EloBracket.Master);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ALL")]
    [InlineData("garbage")]
    public void ResolveFilter_ReturnsNullForTheEveryTierCase(string? filter)
    {
        EloBracket.ResolveFilter(filter).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ALL")]
    [InlineData("all")]
    [InlineData("garbage")]
    public void IsAll_TreatsBlankAllAndUnrecognisedAsEveryGame(string? filter)
    {
        EloBracket.IsAll(filter).Should().BeTrue();
    }

    [Theory]
    [InlineData("GOLD")]
    [InlineData("GOLD_PLUS")]
    [InlineData("MASTER")]
    public void IsAll_IsFalseForASpecificTierFilter(string filter)
    {
        EloBracket.IsAll(filter).Should().BeFalse();
    }

    [Fact]
    public void Persisted_IsTheLadderPlusUnrankedAndExcludesAll()
    {
        EloBracket.Persisted.Should().NotContain(EloBracket.All);
        EloBracket.Persisted.Should().BeEquivalentTo(
        [
            EloBracket.Iron,
            EloBracket.Bronze,
            EloBracket.Silver,
            EloBracket.Gold,
            EloBracket.Platinum,
            EloBracket.Emerald,
            EloBracket.Diamond,
            EloBracket.Master,
            EloBracket.Unranked
        ]);
    }

    [Fact]
    public void Ladder_IsAscendingAndOmitsUnranked()
    {
        EloBracket.Ladder.Should().Equal(
            EloBracket.Iron,
            EloBracket.Bronze,
            EloBracket.Silver,
            EloBracket.Gold,
            EloBracket.Platinum,
            EloBracket.Emerald,
            EloBracket.Diamond,
            EloBracket.Master);
        EloBracket.Ladder.Should().NotContain(EloBracket.Unranked);
    }
}
