using AwesomeAssertions;
using Core.Lol.Map;
using Data.Aggregation;

namespace TrueMain.UnitTests;

public sealed class LanePositionsTests
{
    [Theory]
    [InlineData("BOTTOM", "BOTTOM")]
    [InlineData("UTILITY", "UTILITY")]
    [InlineData("top", "TOP")]
    [InlineData("  middle  ", "MIDDLE")]
    [InlineData("mid", "MIDDLE")]
    [InlineData("BOT", "BOTTOM")]
    public void Normalize_CanonicalisesKnownPositionsAndShortForms(string input, string expected)
        => LanePositions.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("INVALID")]
    [InlineData("ADC")]
    [InlineData("SUPPORT")]
    public void Normalize_ReturnsNullForUnknown(string? input)
        => LanePositions.Normalize(input).Should().BeNull();

    [Theory]
    [InlineData("TOP", true)]
    [InlineData("UTILITY", true)]
    [InlineData("top", false)]
    [InlineData("MID", false)]
    [InlineData(" TOP", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsLane_IsAnExactMatchOnStoredValues(string? input, bool expected)
        => LanePositions.IsLane(input).Should().Be(expected);

    [Fact]
    public void CohortPositionsAreTheLanePositions()
        => ChampionCohort.CanonicalPositions.Should().Equal(LanePositions.All);
}
