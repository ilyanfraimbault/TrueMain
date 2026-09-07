using AwesomeAssertions;
using TrueMain.Http;

namespace TrueMain.UnitTests;

public sealed class PlatformParameterTests
{
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
        PlatformParameter.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("ZZ9")]
    [InlineData("123")]
    [InlineData("not-a-platform")]
    public void NormalizePlatform_ReturnsNull_ForUnknownRoutes(string input)
    {
        PlatformParameter.Normalize(input).Should().BeNull();
    }
}
