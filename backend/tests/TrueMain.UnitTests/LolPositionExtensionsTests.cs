using Core.Lol.Map;
using AwesomeAssertions;

namespace TrueMain.UnitTests;

public sealed class LolPositionExtensionsTests
{
    [Theory]
    [InlineData("BOTTOM", "BOTTOM")]
    [InlineData("UTILITY", "UTILITY")]
    [InlineData("top", "TOP")]
    [InlineData("  middle  ", "MIDDLE")]
    public void Parse_then_ToRiotString_normalizes_known_positions(string input, string expected)
    {
        LolPositionExtensions.Parse(input).ToRiotString().Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("INVALID")]
    public void Parse_then_ToRiotString_returns_null_for_unknown(string? input)
    {
        LolPositionExtensions.Parse(input).ToRiotString().Should().BeNull();
    }
}
