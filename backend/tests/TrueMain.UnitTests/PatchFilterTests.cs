using AwesomeAssertions;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

public sealed class PatchFilterTests
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
    public void Normalize_TrimsTrailingSegmentsToMajorMinor(string? input, string? expected)
    {
        PatchFilter.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Prefix_IsNullWhenThereIsNoPatchFilter()
    {
        PatchFilter.Prefix(null).Should().BeNull();
    }

    [Fact]
    public void Prefix_MatchesEveryHotfixBuildOfThePatch()
    {
        // matches.game_version holds the full Riot version, so the filter is a
        // prefix; the dot guards against "16.4" also matching "16.40".
        PatchFilter.Prefix("16.4").Should().Be("16.4.%");
    }

    [Fact]
    public void NormalizedPrefix_ComposesBothSteps()
    {
        PatchFilter.NormalizedPrefix("16.4.521.123").Should().Be("16.4.%");
        PatchFilter.NormalizedPrefix("nonsense").Should().BeNull();
    }
}
