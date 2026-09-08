using AwesomeAssertions;
using TrueMain.Http;

namespace TrueMain.UnitTests;

public sealed class EloBracketParameterTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("ALL", "ALL")]
    [InlineData("all", "ALL")]
    [InlineData("gold", "GOLD")]
    [InlineData("  gold_plus  ", "GOLD_PLUS")]
    [InlineData("DIAMOND_PLUS", "DIAMOND_PLUS")]
    public void TryNormalizeEloBracket_AcceptsBlankAndRecognisedFilters(string? input, string? expected)
    {
        EloBracketParameter.TryNormalize(input, out var normalized).Should().BeTrue();
        normalized.Should().Be(expected);
    }

    [Theory]
    [InlineData("GOLDD")]
    [InlineData("UNRANKED")]
    [InlineData("gold_minus")]
    [InlineData("not-a-bracket")]
    public void TryNormalizeEloBracket_RejectsAValueThatIsNotABracket(string input)
    {
        // The bug (#1224): these used to normalise to null, which every service
        // reads as "every bracket" — so a typo answered with the whole population
        // under a rank label instead of failing loudly.
        EloBracketParameter.TryNormalize(input, out var normalized).Should().BeFalse();
        normalized.Should().BeNull();
    }
}
