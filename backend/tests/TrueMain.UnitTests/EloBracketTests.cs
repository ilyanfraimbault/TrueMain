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
    [InlineData("MASTER", EloBracket.Master)]
    [InlineData("GRANDMASTER", EloBracket.Grandmaster)]
    [InlineData("CHALLENGER", EloBracket.Challenger)]
    public void FromTier_MapsEachRiotTierToItsOwnBucket(string tier, string expected)
    {
        EloBracket.FromTier(tier).Should().Be(expected);
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
    public void TryResolveFilter_BareTier_YieldsThatTierOnly()
    {
        EloBracket.TryResolveFilter("GOLD", out var bands).Should().BeTrue();
        bands.Should().Equal(EloBracket.Gold);
    }

    [Fact]
    public void TryResolveFilter_TierPlus_YieldsThatTierAndEveryTierAbove()
    {
        EloBracket.TryResolveFilter("GOLD_PLUS", out var bands).Should().BeTrue();
        bands.Should().Equal(
            EloBracket.Gold,
            EloBracket.Platinum,
            EloBracket.Emerald,
            EloBracket.Diamond,
            EloBracket.Master,
            EloBracket.Grandmaster,
            EloBracket.Challenger);
    }

    [Fact]
    public void TryResolveFilter_MasterPlus_UnionsTheApexTiers()
    {
        // Master / Grandmaster / Challenger are now distinct buckets, so
        // "Master and above" spans all three.
        EloBracket.TryResolveFilter("MASTER_PLUS", out var bands).Should().BeTrue();
        bands.Should().Equal(
            EloBracket.Master,
            EloBracket.Grandmaster,
            EloBracket.Challenger);
    }

    [Fact]
    public void TryResolveFilter_ChallengerPlus_CollapsesToChallengerAlone()
    {
        // Challenger tops the ladder, so "and above" adds nothing.
        EloBracket.TryResolveFilter("CHALLENGER_PLUS", out var bands).Should().BeTrue();
        bands.Should().Equal(EloBracket.Challenger);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ALL")]
    public void TryResolveFilter_YieldsNoBandsForTheEveryTierCase(string? filter)
    {
        EloBracket.TryResolveFilter(filter, out var bands).Should().BeTrue();
        bands.Should().BeNull("blank and ALL are the only inputs that earn every bucket");
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("GOLDD")]
    [InlineData("UNRANKED")]
    [InlineData("GOLD_MINUS")]
    public void TryResolveFilter_RejectsAValueThatIsNotABracket(string filter)
    {
        // The bug (#1224): this used to be indistinguishable from ALL, so
        // ?elo=GOLDD served every bracket's games under a Gold label.
        EloBracket.TryResolveFilter(filter, out var bands).Should().BeFalse();
        bands.Should().BeNull();
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("GOLDD")]
    public void ResolveFilterOrEmpty_DegradesARejectedFilterToNoBandAtAll(string filter)
    {
        // Empty, not null: null is "no restriction" and would widen the read to
        // the whole population, which is the failure being guarded.
        EloBracket.ResolveFilterOrEmpty(filter).Should().NotBeNull().And.BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ALL")]
    public void ResolveFilterOrEmpty_StillMeansEveryBucketForTheEveryTierCase(string? filter)
    {
        EloBracket.ResolveFilterOrEmpty(filter).Should().BeNull();
    }

    [Fact]
    public void ResolveFilterOrEmpty_ResolvesARecognisedFilterNormally()
    {
        EloBracket.ResolveFilterOrEmpty("gold").Should().Equal(EloBracket.Gold);
    }

    [Theory]
    [InlineData("gold", EloBracket.Gold)]
    [InlineData(" gold_plus ", "GOLD_PLUS")]
    [InlineData("DIAMOND_PLUS", "DIAMOND_PLUS")]
    public void ResolveToken_UsesTheCanonicalFormForRecognisedFilters(string raw, string expected)
    {
        EloBracket.ResolveToken(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ALL")]
    public void ResolveToken_IsAllForTheEveryTierCase(string? filter)
    {
        // Mirrors TryResolveFilter's no-bands cases so the cache token and the
        // band set can never disagree on what "every bucket" means.
        EloBracket.ResolveToken(filter).Should().Be("all");
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("GOLDD")]
    public void ResolveToken_GivesARejectedFilterItsOwnToken(string filter)
    {
        // A rejected filter answers empty where ALL answers with the whole
        // population, so the two must never share a cache entry. One shared
        // token for all of them, so garbage cannot mint unbounded entries.
        EloBracket.ResolveToken(filter).Should().Be(EloBracket.InvalidToken);
        EloBracket.ResolveToken(filter).Should().NotBe("all");
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
            EloBracket.Grandmaster,
            EloBracket.Challenger,
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
            EloBracket.Master,
            EloBracket.Grandmaster,
            EloBracket.Challenger);
        EloBracket.Ladder.Should().NotContain(EloBracket.Unranked);
    }
}
