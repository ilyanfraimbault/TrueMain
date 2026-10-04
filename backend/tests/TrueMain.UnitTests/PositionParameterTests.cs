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
    [InlineData("mid", "MIDDLE")]
    [InlineData(" Bot ", "BOTTOM")]
    public void NormalizePosition_UppercasesAndValidatesAgainstKnownPositions(string? input, string? expected)
    {
        PositionParameter.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("adc")]
    [InlineData("support")]
    [InlineData("not-a-position")]
    public void NormalizePosition_ReturnsNull_ForUnknownPositions(string input)
    {
        // Only MID and BOT are accepted as short forms (they abbreviate the
        // canonical word); role names like "adc" or "support" are rejected so
        // the query layer can distinguish "client typo" from "no filter".
        PositionParameter.Normalize(input).Should().BeNull();
    }
}
