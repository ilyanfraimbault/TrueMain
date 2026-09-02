using AwesomeAssertions;
using Data.Entities;

namespace TrueMain.UnitTests;

/// <summary>
/// #1360: the games-owed rule is applied in two places — the claim's ordering and the size of
/// the match-ids request — so the cases where it must answer "zero" rather than a number are
/// pinned here rather than restated at each call site.
/// </summary>
public sealed class LadderGamesOwedTests
{
    [Fact]
    public void From_ReturnsTheGamesPlayedSinceTheBaseline()
        => LadderGamesOwed.From(545, 500).Should().Be(45);

    [Fact]
    public void From_ReturnsZero_WhenTheBaselineIsUnknown()
        // Reading a missing baseline as zero would report the player's entire season as owed —
        // and right after a deploy that is every tracked account at once, which would order the
        // pool by career volume instead of by recent activity.
        => LadderGamesOwed.From(900, null).Should().Be(0);

    [Fact]
    public void From_ReturnsZero_WhenNoLadderReadingExists()
        => LadderGamesOwed.From(null, 500).Should().Be(0);

    [Fact]
    public void From_ReturnsZero_AcrossASeasonReset()
        // Wins and losses restart from the bottom, so the difference goes negative for every
        // account simultaneously.
        => LadderGamesOwed.From(12, 800).Should().Be(0);

    [Fact]
    public void From_ReturnsZero_WhenNothingWasPlayed()
        => LadderGamesOwed.From(500, 500).Should().Be(0);
}
