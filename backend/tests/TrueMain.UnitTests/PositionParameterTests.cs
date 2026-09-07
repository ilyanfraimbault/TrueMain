using AwesomeAssertions;
using TrueMain.Http;

namespace TrueMain.UnitTests;

public sealed class PositionParameterTests
{
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
        PositionParameter.Normalize(input).Should().Be(expected);
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
        PositionParameter.Normalize(input).Should().BeNull();
    }
}
