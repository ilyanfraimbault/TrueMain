using Data.Entities;
using FluentAssertions;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

public sealed class ChampionBuildTreeBuilderTests
{
    [Fact]
    public void Build_ShouldComputeRootAndChildPickRatesFromSummedCounters()
    {
        var rows = new[]
        {
            BuildAggregateBuild([3153, 3006], games: 7, wins: 4),
            BuildAggregateBuild([3153, 3091], games: 3, wins: 2)
        };

        var tree = ChampionBuildTreeBuilder.Build(rows, totalGames: 10, maxDepth: 7, minBranchGames: 1);

        tree.Should().HaveCount(1);
        tree[0].ItemId.Should().Be(3153);
        tree[0].Games.Should().Be(10);
        tree[0].PickRate.Should().BeApproximately(1.0, 0.0001);
        tree[0].Children.Should().HaveCount(2);
        tree[0].Children[0].ItemId.Should().Be(3006);
        tree[0].Children[0].Games.Should().Be(7);
        tree[0].Children[0].PickRate.Should().BeApproximately(0.7, 0.0001);
        tree[0].Children[1].ItemId.Should().Be(3091);
        tree[0].Children[1].Games.Should().Be(3);
        tree[0].Children[1].PickRate.Should().BeApproximately(0.3, 0.0001);
    }

    [Fact]
    public void Build_ShouldKeepBootsAtAnyValidPosition()
    {
        var rows = new[]
        {
            BuildAggregateBuild([3153, 3006, 6672], games: 2, wins: 1),
            BuildAggregateBuild([3153, 6672, 3006], games: 1, wins: 1)
        };

        var tree = ChampionBuildTreeBuilder.Build(rows, totalGames: 3, maxDepth: 7, minBranchGames: 1);

        tree.Single().Children.Select(node => node.ItemId).Should().Contain([3006, 6672]);
    }

    [Fact]
    public void Build_ShouldAttachTopRunePageToEachRootFirstItemOnly()
    {
        // Two first items (3153, 6671). Rune pages split so that 3153-root
        // players overwhelmingly run keystone 8005, while 6671-root players
        // overwhelmingly run keystone 8010. Children must not carry a rune
        // page (the correlation only holds at depth 1).
        var rows = new[]
        {
            BuildAggregateBuild([3153, 3006], games: 7, wins: 4),
            BuildAggregateBuild([6671, 3006], games: 3, wins: 2)
        };
        var runePages = new[]
        {
            BuildAggregateRunePage(firstItemId: 3153, keystoneId: 8005, games: 6, wins: 3),
            BuildAggregateRunePage(firstItemId: 3153, keystoneId: 8010, games: 1, wins: 1),
            BuildAggregateRunePage(firstItemId: 6671, keystoneId: 8010, games: 3, wins: 2)
        };

        var tree = ChampionBuildTreeBuilder.Build(rows, totalGames: 10, maxDepth: 7, minBranchGames: 1, runePages);

        tree.Should().HaveCount(2);
        var rush3153 = tree.Single(node => node.ItemId == 3153);
        rush3153.RunePage.Should().NotBeNull();
        rush3153.RunePage!.FirstItemId.Should().Be(3153);
        rush3153.RunePage.PrimaryKeystoneId.Should().Be(8005, because: "8005 dominated 3153-rushers (6/7 games)");
        rush3153.Children[0].RunePage.Should().BeNull(because: "runes are attached at the root only");

        var rush6671 = tree.Single(node => node.ItemId == 6671);
        rush6671.RunePage.Should().NotBeNull();
        rush6671.RunePage!.PrimaryKeystoneId.Should().Be(8010, because: "only 8010 was played with 6671 rush");
    }

    [Fact]
    public void Build_ShouldOmitRunePageWhenFirstItemHasNoCorrelatedRows()
    {
        // Build tree has one root (3153) but rune pages only know about
        // a different item (e.g. because the migration backfilled with
        // FirstItemId=0 and no post-deploy aggregation has run yet).
        var rows = new[] { BuildAggregateBuild([3153, 3006], games: 5, wins: 3) };
        var runePages = new[]
        {
            BuildAggregateRunePage(firstItemId: 0, keystoneId: 8005, games: 5, wins: 3)
        };

        var tree = ChampionBuildTreeBuilder.Build(rows, totalGames: 5, maxDepth: 7, minBranchGames: 1, runePages);

        tree.Should().HaveCount(1);
        tree[0].RunePage.Should().BeNull();
    }

    [Fact]
    public void Build_ShouldPruneBranchesBelowMinimumSupportAndRespectMaxDepth()
    {
        var rows = new[]
        {
            BuildAggregateBuild([3153, 3006, 6672], games: 5, wins: 3),
            BuildAggregateBuild([3153, 3091, 3085], games: 1, wins: 0)
        };

        var tree = ChampionBuildTreeBuilder.Build(rows, totalGames: 6, maxDepth: 2, minBranchGames: 2);

        tree.Should().HaveCount(1);
        tree[0].Children.Should().HaveCount(1);
        tree[0].Children[0].ItemId.Should().Be(3006);
        tree[0].Children[0].Children.Should().BeEmpty();
    }

    private static ChampionAggregateRunePage BuildAggregateRunePage(int firstItemId, int keystoneId, int games, int wins)
        => new()
        {
            FirstItemId = firstItemId,
            PrimaryStyleId = 8000,
            PrimaryKeystoneId = keystoneId,
            SecondaryStyleId = 8100,
            Games = games,
            Wins = wins
        };

    private static ChampionAggregateBuild BuildAggregateBuild(IReadOnlyList<int> buildItems, int games, int wins)
    {
        var build = buildItems.Concat(Enumerable.Repeat(0, 7)).Take(7).ToArray();

        return new ChampionAggregateBuild
        {
            BootsItemId = 0,
            BuildItem0 = build[0],
            BuildItem1 = build[1],
            BuildItem2 = build[2],
            BuildItem3 = build[3],
            BuildItem4 = build[4],
            BuildItem5 = build[5],
            BuildItem6 = build[6],
            Games = games,
            Wins = wins
        };
    }
}
