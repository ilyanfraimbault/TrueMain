using AwesomeAssertions;
using TrueMain.Services.Truemains;

namespace TrueMain.UnitTests;

/// <summary>
/// Covers the pure half of the "you vs mains" comparison (issue #529): picking
/// each pool's dominant choice, walking its dominant item path, and counting
/// how much of one pool made the other pool's choice.
/// </summary>
public sealed class BuildDivergenceAnalyzerTests
{
    private static readonly Guid StarterA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid StarterB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SkillA = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void TopChoice_returns_the_most_played_key()
    {
        var rows = new[]
        {
            Row(starter: StarterA, games: 4, wins: 2),
            Row(starter: StarterB, games: 9, wins: 3),
        };

        var top = BuildDivergenceAnalyzer.TopChoice(rows, row => row.StarterItemsId);

        top.Should().NotBeNull();
        top!.Value.Key.Should().Be(StarterB);
        top.Value.Games.Should().Be(9);
        top.Value.Wins.Should().Be(3);
    }

    [Fact]
    public void TopChoice_sums_every_row_sharing_a_key()
    {
        var rows = new[]
        {
            Row(starter: StarterA, games: 4, wins: 2),
            Row(starter: StarterA, games: 3, wins: 3),
            Row(starter: StarterB, games: 6, wins: 1),
        };

        var top = BuildDivergenceAnalyzer.TopChoice(rows, row => row.StarterItemsId);

        top!.Value.Key.Should().Be(StarterA, "4 + 3 games beats 6");
        top.Value.Games.Should().Be(7);
        top.Value.Wins.Should().Be(5);
    }

    [Fact]
    public void TopChoice_breaks_a_games_tie_on_wins_then_on_the_key()
    {
        var rows = new[]
        {
            Row(starter: StarterB, games: 5, wins: 1),
            Row(starter: StarterA, games: 5, wins: 4),
        };

        BuildDivergenceAnalyzer.TopChoice(rows, row => row.StarterItemsId)!.Value.Key
            .Should().Be(StarterA, "equal games break on wins");

        var allEqual = new[]
        {
            Row(starter: StarterB, games: 5, wins: 2),
            Row(starter: StarterA, games: 5, wins: 2),
        };

        BuildDivergenceAnalyzer.TopChoice(allEqual, row => row.StarterItemsId)!.Value.Key
            .Should().Be(StarterA, "a full tie falls back to the key so the answer is stable");
    }

    [Fact]
    public void TopChoice_returns_null_on_an_empty_pool()
    {
        BuildDivergenceAnalyzer.TopChoice([], row => row.StarterItemsId).Should().BeNull();
    }

    [Fact]
    public void TotalsForKey_counts_only_the_rows_on_that_key()
    {
        var rows = new[]
        {
            Row(starter: StarterA, games: 4, wins: 2),
            Row(starter: StarterA, games: 2, wins: 0),
            Row(starter: StarterB, games: 9, wins: 5),
        };

        var (games, wins) = BuildDivergenceAnalyzer.TotalsForKey(rows, row => row.StarterItemsId, StarterA);

        games.Should().Be(6);
        wins.Should().Be(2);
    }

    [Fact]
    public void TotalsForKey_returns_zero_for_a_key_nobody_picked()
    {
        var rows = new[] { Row(starter: StarterB, games: 9, wins: 5) };

        BuildDivergenceAnalyzer.TotalsForKey(rows, row => row.StarterItemsId, StarterA)
            .Should().Be((0, 0));
    }

    [Fact]
    public void WalkCorePath_follows_the_dominant_item_at_every_depth()
    {
        var rows = new[]
        {
            Path([3153, 3031, 3072], games: 7, wins: 4),
            Path([3153, 3031, 3036], games: 2, wins: 1),
            Path([6673, 3031, 3072], games: 1, wins: 0),
        };

        var path = BuildDivergenceAnalyzer.WalkCorePath(rows);

        path.ItemIds.Should().Equal(3153, 3031, 3072);
        path.Games.Should().Be(7, "seven games followed the whole chain");
        path.Wins.Should().Be(4);
    }

    [Fact]
    public void WalkCorePath_stops_at_three_items_even_when_the_build_runs_deeper()
    {
        var rows = new[] { Path([3153, 3031, 3072, 3026, 3139], games: 10, wins: 6) };

        BuildDivergenceAnalyzer.WalkCorePath(rows).ItemIds
            .Should().Equal(3153, 3031, 3072);
    }

    [Fact]
    public void WalkCorePath_stops_where_the_pool_has_no_dominant_next_item()
    {
        // Six equally popular ways to follow 3153: each takes 4 of the 24 games
        // that reached it (~17%), so none clears the 20% step floor.
        var rows = new[]
        {
            Path([3153, 1001], games: 4, wins: 2),
            Path([3153, 1002], games: 4, wins: 2),
            Path([3153, 1003], games: 4, wins: 2),
            Path([3153, 1004], games: 4, wins: 2),
            Path([3153, 1005], games: 4, wins: 2),
            Path([3153, 1006], games: 4, wins: 2),
        };

        var path = BuildDivergenceAnalyzer.WalkCorePath(rows);

        path.ItemIds.Should().Equal(
            new[] { 3153 }, "no second item is taken by 20% of the games that bought 3153");
        path.Games.Should().Be(24);
    }

    [Fact]
    public void WalkCorePath_counts_short_builds_in_the_denominator()
    {
        // Only 2 of 10 games reached a second item — exactly the 20% floor, so
        // the step survives, but the reported support is those 2 games, not 10.
        var rows = new[]
        {
            Path([3153], games: 8, wins: 4),
            Path([3153, 3031], games: 2, wins: 2),
        };

        var path = BuildDivergenceAnalyzer.WalkCorePath(rows);

        path.ItemIds.Should().Equal(3153, 3031);
        path.Games.Should().Be(2);
        path.Wins.Should().Be(2);
    }

    [Fact]
    public void WalkCorePath_ignores_games_that_completed_no_item()
    {
        var rows = new[]
        {
            Path([], games: 50, wins: 25),
            Path([3153, 3031], games: 4, wins: 3),
        };

        var path = BuildDivergenceAnalyzer.WalkCorePath(rows);

        path.ItemIds.Should().Equal(
            new[] { 3153, 3031 }, "the item-less games are not a build preference");
        path.Games.Should().Be(4);
    }

    [Fact]
    public void WalkCorePath_returns_an_empty_path_when_no_game_completed_an_item()
    {
        var path = BuildDivergenceAnalyzer.WalkCorePath([Path([], games: 12, wins: 6)]);

        path.ItemIds.Should().BeEmpty();
        path.Games.Should().Be(0);
        path.Wins.Should().Be(0);
    }

    [Fact]
    public void TotalsForPath_counts_builds_that_start_with_the_path()
    {
        var rows = new[]
        {
            Path([3153, 3031, 3072], games: 5, wins: 3),
            Path([3153, 3031, 3036], games: 4, wins: 1),
            Path([3153, 3006], games: 6, wins: 2),
        };

        var (games, wins) = BuildDivergenceAnalyzer.TotalsForPath(rows, [3153, 3031]);

        games.Should().Be(9, "both 3153 → 3031 continuations count, the 3153 → 3006 one does not");
        wins.Should().Be(4);
    }

    [Fact]
    public void TotalsForPath_requires_the_same_order()
    {
        var rows = new[] { Path([3031, 3153], games: 8, wins: 5) };

        BuildDivergenceAnalyzer.TotalsForPath(rows, [3153, 3031])
            .Should().Be((0, 0), "the same two items in the other order is a different build");
    }

    [Fact]
    public void TotalsForPath_counts_nothing_for_an_empty_path()
    {
        var rows = new[] { Path([3153, 3031], games: 8, wins: 5) };

        BuildDivergenceAnalyzer.TotalsForPath(rows, []).Should().Be((0, 0));
    }

    private static DivergencePatternRow Row(
        int games,
        int wins,
        Guid? starter = null,
        Guid? skill = null,
        int boots = 0)
        => new(
            starter ?? StarterA,
            skill ?? SkillA,
            boots,
            0, 0, 0, 0, 0, 0, 0,
            games,
            wins);

    private static DivergencePatternRow Path(IReadOnlyList<int> items, int games, int wins)
        => new(
            StarterA,
            SkillA,
            0,
            At(items, 0), At(items, 1), At(items, 2), At(items, 3),
            At(items, 4), At(items, 5), At(items, 6),
            games,
            wins);

    private static int At(IReadOnlyList<int> items, int index)
        => index < items.Count ? items[index] : 0;
}
