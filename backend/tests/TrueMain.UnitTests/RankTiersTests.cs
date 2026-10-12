using AwesomeAssertions;
using Core.Lol.Ranking;
using Data.Configurations;

namespace TrueMain.UnitTests;

/// <summary>
/// #239: the rank columns keep the text they held before the enums existed, so the
/// converters must round-trip Riot's spelling exactly — a drift here would rewrite or
/// fail to read every stored snapshot.
/// </summary>
public sealed class RankTiersTests
{
    [Fact]
    public void EveryTier_RoundTripsThroughItsRiotName_InLadderOrder()
    {
        Enum.GetValues<RankTier>().Select(tier => tier.ToRiotName()).Should().Equal(EloBracket.Ladder);

        foreach (var tier in Enum.GetValues<RankTier>())
        {
            RankTiers.ParseTier(tier.ToRiotName()).Should().Be(tier);
        }
    }

    [Theory]
    [InlineData("I", RankDivision.I)]
    [InlineData("II", RankDivision.II)]
    [InlineData("III", RankDivision.III)]
    [InlineData("IV", RankDivision.IV)]
    public void Division_RoundTripsThroughItsNumeral(string stored, RankDivision division)
    {
        RankTiers.ParseDivision(stored).Should().Be(division);
        division.ToRiotName().Should().Be(stored);
    }

    [Theory]
    [InlineData(" diamond ", RankTier.Diamond)]
    [InlineData("Grandmaster", RankTier.Grandmaster)]
    public void TryParseTier_IgnoresCaseAndWhitespace(string value, RankTier expected)
    {
        RankTiers.TryParseTier(value, out var tier).Should().BeTrue();
        tier.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("UNRANKED")]
    [InlineData("ALL")]
    [InlineData("GM")]
    public void TryParseTier_RejectsWhatIsNotARiotTier(string? value)
    {
        RankTiers.TryParseTier(value, out _).Should().BeFalse();
    }

    [Fact]
    public void Converters_StoreRiotSpelling()
    {
        new RankTierConverter().ConvertToProvider(RankTier.Emerald).Should().Be("EMERALD");
        new RankTierConverter().ConvertFromProvider("EMERALD").Should().Be(RankTier.Emerald);
        new RankDivisionConverter().ConvertToProvider(RankDivision.III).Should().Be("III");
        new RankDivisionConverter().ConvertFromProvider("III").Should().Be(RankDivision.III);
    }

    [Fact]
    public void DivisionConverter_ReadsABlankDivisionAsTheApexDivision()
    {
        new RankDivisionConverter().ConvertFromProvider(string.Empty).Should().Be(RankDivision.I);
    }

    [Fact]
    public void TierConverter_RefusesAValueThatIsNotATier()
    {
        var read = () => new RankTierConverter().ConvertFromProvider("UNRANKED");

        read.Should().Throw<FormatException>();
    }
}
