using AwesomeAssertions;
using TrueMain.Services.Truemains.Leaderboard;
using Line = TrueMain.Services.Truemains.Leaderboard.LeaderboardStatLines.Line;

namespace TrueMain.UnitTests;

public sealed class LeaderboardStatLinesTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid D = Guid.Parse("00000000-0000-0000-0000-00000000000d");

    [Fact]
    public void Games_SortsDescendingAndBreaksTiesOnRankScore()
    {
        var lines = new[]
        {
            new Line(A, Score: 100, Games: 20, 0, 0, 0, null, null),
            new Line(B, Score: 300, Games: 20, 0, 0, 0, null, null),
            new Line(C, Score: 200, Games: 50, 0, 0, 0, null, null),
        };

        LeaderboardStatLines.Order(lines, LeaderboardSort.Games).Should().Equal(C, B, A);
    }

    [Fact]
    public void Kda_UsesTheRowFormulaAndSinksAccountsWithoutGames()
    {
        var lines = new[]
        {
            // Deathless: (5 + 5) / 2 games = 5, the same fallback the row prints.
            new Line(A, Score: 100, Games: 2, Kills: 5, Deaths: 0, Assists: 5, null, null),
            new Line(B, Score: 100, Games: 10, Kills: 30, Deaths: 10, Assists: 30, null, null),
            // No aggregate games: no KDA on the row, so last — even with a top score.
            new Line(C, Score: 900, Games: 0, 0, 0, 0, null, null),
        };

        LeaderboardStatLines.Order(lines, LeaderboardSort.Kda).Should().Equal(B, A, C);
    }

    [Fact]
    public void WinRate_ReadsTheSnapshotSplitAndSinksAMissingOne()
    {
        var lines = new[]
        {
            new Line(A, Score: 100, Games: 0, 0, 0, 0, Wins: 55, Losses: 45),
            new Line(B, Score: 900, Games: 0, 0, 0, 0, Wins: null, Losses: null),
            new Line(C, Score: 100, Games: 0, 0, 0, 0, Wins: 0, Losses: 0),
            new Line(D, Score: 100, Games: 0, 0, 0, 0, Wins: 40, Losses: 60),
        };

        // C has a 0-0 split: no win rate, like the row's em dash. B and C are
        // both missing, so the rank score orders them.
        LeaderboardStatLines.Order(lines, LeaderboardSort.WinRate).Should().Equal(A, D, B, C);
    }

    [Fact]
    public void NonStatSortsAreRejected()
    {
        var act = () => LeaderboardStatLines.Order([], LeaderboardSort.Rank);

        act.Should().Throw<ArgumentOutOfRangeException>();
        LeaderboardStatLines.Serves(LeaderboardSort.Dedication).Should().BeFalse();
        LeaderboardStatLines.Serves(LeaderboardSort.WinRate).Should().BeTrue();
    }
}
