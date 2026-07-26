using AwesomeAssertions;
using Core.Lol.Map;

namespace TrueMain.UnitTests;

/// <summary>
/// Pure-logic coverage for the "one gap, one unresolved member" inference used
/// by both <c>RiotMatchMapper</c> (ingestion-time) and
/// <c>MatchTeamPositionCorrectionProcess</c> (backfill). The contract is
/// deliberately conservative: only the single-gap/single-unresolved shape is
/// ever resolved, everything else is left alone.
/// </summary>
public sealed class TeamPositionInferrerTests
{
    [Fact]
    public void TryInferSingleMissingPosition_ResolvesTheOneGap()
    {
        var positions = new[] { "TOP", "JUNGLE", "", "BOTTOM", "UTILITY" };

        var resolved = TeamPositionInferrer.TryInferSingleMissingPosition(
            positions, out var unresolvedIndex, out var inferredPosition);

        resolved.Should().BeTrue();
        unresolvedIndex.Should().Be(2);
        inferredPosition.Should().Be("MIDDLE");
    }

    [Fact]
    public void TryInferSingleMissingPosition_TreatsAnUnrecognisedValue_LikeBlank()
    {
        var positions = new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "Invalid" };

        var resolved = TeamPositionInferrer.TryInferSingleMissingPosition(
            positions, out var unresolvedIndex, out var inferredPosition);

        resolved.Should().BeTrue();
        unresolvedIndex.Should().Be(4);
        inferredPosition.Should().Be("UTILITY");
    }

    [Fact]
    public void TryInferSingleMissingPosition_IsCaseInsensitiveOnKnownLanes()
    {
        var positions = new[] { "top", "jungle", "middle", "bottom", "" };

        var resolved = TeamPositionInferrer.TryInferSingleMissingPosition(
            positions, out _, out var inferredPosition);

        resolved.Should().BeTrue();
        inferredPosition.Should().Be("UTILITY");
    }

    [Fact]
    public void TryInferSingleMissingPosition_ReturnsFalse_WhenNoGap()
    {
        var positions = new[] { "TOP", "JUNGLE", "MIDDLE", "BOTTOM", "UTILITY" };

        var resolved = TeamPositionInferrer.TryInferSingleMissingPosition(
            positions, out var unresolvedIndex, out var inferredPosition);

        resolved.Should().BeFalse();
        unresolvedIndex.Should().Be(-1);
        inferredPosition.Should().BeEmpty();
    }

    [Fact]
    public void TryInferSingleMissingPosition_ReturnsFalse_WhenMultipleMembersUnresolved()
    {
        // Two gaps (MIDDLE and UTILITY), two unresolved members — ambiguous which
        // unresolved member goes where, so neither is guessed.
        var positions = new[] { "TOP", "JUNGLE", "", "BOTTOM", "" };

        var resolved = TeamPositionInferrer.TryInferSingleMissingPosition(
            positions, out var unresolvedIndex, out var inferredPosition);

        resolved.Should().BeFalse();
        unresolvedIndex.Should().Be(-1);
        inferredPosition.Should().BeEmpty();
    }

    [Fact]
    public void TryInferSingleMissingPosition_ReturnsFalse_WhenAllPositionsUnresolved()
    {
        // e.g. an ARAM team, which never carries lane data at all.
        var positions = new[] { "", "", "", "", "" };

        var resolved = TeamPositionInferrer.TryInferSingleMissingPosition(
            positions, out _, out _);

        resolved.Should().BeFalse();
    }

    [Fact]
    public void TryInferSingleMissingPosition_ReturnsFalse_WhenALaneIsDuplicated()
    {
        // TOP is duplicated and UTILITY is missing, but every member already has a
        // recognised position — there's no unresolved member to reassign.
        var positions = new[] { "TOP", "TOP", "JUNGLE", "MIDDLE", "BOTTOM" };

        var resolved = TeamPositionInferrer.TryInferSingleMissingPosition(
            positions, out _, out _);

        resolved.Should().BeFalse();
    }
}
