using AwesomeAssertions;
using TrueMain.Controllers.Champions;

namespace TrueMain.UnitTests;

public sealed class ChampionQueryParameterNormalizerTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("16.4", "16.4")]
    [InlineData("16.4.521", "16.4")]
    [InlineData("16.4.521.123", "16.4")]
    [InlineData("  16.4.521  ", "16.4")]
    [InlineData("16", "16")]
    public void NormalizePatch_TrimsTrailingSegmentsToMajorMinor(string? input, string? expected)
    {
        ChampionQueryParameterNormalizer.NormalizePatch(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("EUW1", "EUW1")]
    [InlineData("euw1", "EUW1")]
    [InlineData("  euw1  ", "EUW1")]
    [InlineData("Na1", "NA1")]
    [InlineData("kr", "KR")]
    [InlineData("JP1", "JP1")]
    public void NormalizePlatform_UppercasesAndValidatesAgainstKnownRoutes(string? input, string? expected)
    {
        ChampionQueryParameterNormalizer.NormalizePlatform(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("ZZ9")]
    [InlineData("123")]
    [InlineData("not-a-platform")]
    public void NormalizePlatform_ReturnsNull_ForUnknownRoutes(string input)
    {
        ChampionQueryParameterNormalizer.NormalizePlatform(input).Should().BeNull();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("TOP", "TOP")]
    [InlineData("top", "TOP")]
    [InlineData("  top  ", "TOP")]
    [InlineData("JUNGLE", "JUNGLE")]
    [InlineData("middle", "MIDDLE")]
    [InlineData("BOTTOM", "BOTTOM")]
    [InlineData("utility", "UTILITY")]
    public void NormalizePosition_UppercasesAndValidatesAgainstKnownPositions(string? input, string? expected)
    {
        ChampionQueryParameterNormalizer.NormalizePosition(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("mid")]
    [InlineData("adc")]
    [InlineData("support")]
    [InlineData("not-a-position")]
    public void NormalizePosition_ReturnsNull_ForUnknownPositions(string input)
    {
        // Riot's canonical position vocabulary does not include shorthand
        // like "mid" or "adc" — they're rejected so the query layer can
        // distinguish "client typo" from "no filter".
        ChampionQueryParameterNormalizer.NormalizePosition(input).Should().BeNull();
    }
}
