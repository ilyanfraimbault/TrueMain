using AwesomeAssertions;
using TrueMain.Http;

namespace TrueMain.UnitTests;

public sealed class PatchParameterTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("16.4", "16.4")]
    [InlineData("16.4.521", "16.4")]
    [InlineData("16.4.521.123", "16.4")]
    [InlineData("  16.4.521  ", "16.4")]
    [InlineData("16", null)]
    [InlineData("16.x", null)]
    [InlineData("abc.def", null)]
    public void NormalizePatch_TrimsTrailingSegmentsToMajorMinor(string? input, string? expected)
    {
        PatchParameter.Normalize(input).Should().Be(expected);
    }
}
