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
    public void Normalize_canonicalises_known_positions_and_short_forms(string input, string expected)
        => LanePositions.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("INVALID")]
    [InlineData("ADC")]
    [InlineData("SUPPORT")]
    public void Normalize_returns_null_for_unknown(string? input)
        => LanePositions.Normalize(input).Should().BeNull();

    [Theory]
    [InlineData("TOP", true)]
    [InlineData("UTILITY", true)]
    [InlineData("top", false)]
    [InlineData("MID", false)]
    [InlineData(" TOP", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsLane_is_an_exact_match_on_stored_values(string? input, bool expected)
        => LanePositions.IsLane(input).Should().Be(expected);

    [Fact]
    public void Cohort_positions_are_the_lane_positions()
        => ChampionCohort.CanonicalPositions.Should().Equal(LanePositions.All);
}
