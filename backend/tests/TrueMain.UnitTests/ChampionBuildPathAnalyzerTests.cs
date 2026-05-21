using FluentAssertions;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

public sealed class ChampionBuildPathAnalyzerTests
{
    private const int FirstItemId = 3742;

    [Fact]
    public void WalkPath_picks_the_popular_sibling_over_a_deeper_subtree()
    {
        var sequences = new[]
        {
            Seq(1001, games: 40, wins: 20),
            Seq(1002, 2001, 3001, 4001, games: 30, wins: 15),
        };
        var tree = ChampionBuildPathAnalyzer.BuildItemTree(sequences, sliceGames: 100);

        var (path, games, wins) = ChampionBuildPathAnalyzer.WalkPath(
            tree, FirstItemId, sliceGames: 100, sliceWins: 50);

        path.Should().Equal(FirstItemId, 1001);
        games.Should().Be(40);
        wins.Should().Be(20);
    }

    [Fact]
    public void WalkPath_picks_the_popular_sibling_at_deeper_levels_too()
    {
        var sequences = new[]
        {
            Seq(1001, 2001, 3002, games: 40, wins: 20),
            Seq(1001, 2001, 3001, 4001, 5001, games: 20, wins: 10),
        };
        var tree = ChampionBuildPathAnalyzer.BuildItemTree(sequences, sliceGames: 100);

        var (path, _, _) = ChampionBuildPathAnalyzer.WalkPath(
            tree, FirstItemId, sliceGames: 100, sliceWins: 50);

        path.Should().Equal(FirstItemId, 1001, 2001, 3002);
    }

    [Fact]
    public void WalkPath_breaks_ties_by_subtree_depth_when_games_are_equal()
    {
        var sequences = new[]
        {
            Seq(1001, games: 20, wins: 10),
            Seq(1002, 2001, 3001, games: 20, wins: 10),
        };
        var tree = ChampionBuildPathAnalyzer.BuildItemTree(sequences, sliceGames: 100);

        var (path, _, _) = ChampionBuildPathAnalyzer.WalkPath(
            tree, FirstItemId, sliceGames: 100, sliceWins: 50);

        path.Should().Equal(FirstItemId, 1002, 2001, 3001);
    }

    [Fact]
    public void WalkPath_breaks_ties_by_wins_when_games_and_depth_are_equal()
    {
        var sequences = new[]
        {
            Seq(1001, 2001, games: 20, wins: 15),
            Seq(1002, 2002, games: 20, wins: 5),
        };
        var tree = ChampionBuildPathAnalyzer.BuildItemTree(sequences, sliceGames: 100);

        var (path, _, _) = ChampionBuildPathAnalyzer.WalkPath(
            tree, FirstItemId, sliceGames: 100, sliceWins: 50);

        path.Should().Equal(FirstItemId, 1001, 2001);
    }

    [Fact]
    public void WalkPath_stops_when_the_best_child_is_below_the_20_percent_threshold()
    {
        var sequences = new[]
        {
            Seq(1001, games: 15, wins: 10),
            Seq(1002, games: 10, wins: 5),
            Seq(1003, games: 5, wins: 2),
        };
        var tree = ChampionBuildPathAnalyzer.BuildItemTree(sequences, sliceGames: 100);

        var (path, games, wins) = ChampionBuildPathAnalyzer.WalkPath(
            tree, FirstItemId, sliceGames: 100, sliceWins: 50);

        path.Should().Equal(FirstItemId);
        games.Should().Be(100);
        wins.Should().Be(50);
    }

    [Fact]
    public void WalkPath_returns_the_first_item_alone_when_the_tree_is_empty()
    {
        var tree = ChampionBuildPathAnalyzer.BuildItemTree([], sliceGames: 0);

        var (path, games, wins) = ChampionBuildPathAnalyzer.WalkPath(
            tree, FirstItemId, sliceGames: 100, sliceWins: 50);

        path.Should().Equal(FirstItemId);
        games.Should().Be(100);
        wins.Should().Be(50);
    }

    [Fact]
    public void WalkPath_walks_the_full_six_item_chain_when_every_step_passes_the_threshold()
    {
        var sequences = new[]
        {
            Seq(1001, 2001, 3001, 4001, 5001, 6001, games: 80, wins: 40),
        };
        var tree = ChampionBuildPathAnalyzer.BuildItemTree(sequences, sliceGames: 100);

        var (path, games, wins) = ChampionBuildPathAnalyzer.WalkPath(
            tree, FirstItemId, sliceGames: 100, sliceWins: 50);

        path.Should().Equal(FirstItemId, 1001, 2001, 3001, 4001, 5001, 6001);
        games.Should().Be(80);
        wins.Should().Be(40);
    }

    private static ChampionBuildPathAnalyzer.BuildSequence Seq(
        int item1, int item2 = 0, int item3 = 0, int item4 = 0, int item5 = 0, int item6 = 0,
        int games = 0, int wins = 0) =>
        new(item1, item2, item3, item4, item5, item6, games, wins);
}
