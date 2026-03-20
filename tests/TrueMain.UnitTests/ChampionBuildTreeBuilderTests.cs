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
            BuildAggregate([3153, 3006], games: 7, wins: 4),
            BuildAggregate([3153, 3091], games: 3, wins: 2)
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
            BuildAggregate([3153, 3006, 6672], games: 2, wins: 1),
            BuildAggregate([3153, 6672, 3006], games: 1, wins: 1)
        };

        var tree = ChampionBuildTreeBuilder.Build(rows, totalGames: 3, maxDepth: 7, minBranchGames: 1);

        tree.Single().Children.Select(node => node.ItemId).Should().Contain([3006, 6672]);
    }

    [Fact]
    public void Build_ShouldPruneBranchesBelowMinimumSupportAndRespectMaxDepth()
    {
        var rows = new[]
        {
            BuildAggregate([3153, 3006, 6672], games: 5, wins: 3),
            BuildAggregate([3153, 3091, 3085], games: 1, wins: 0)
        };

        var tree = ChampionBuildTreeBuilder.Build(rows, totalGames: 6, maxDepth: 2, minBranchGames: 2);

        tree.Should().HaveCount(1);
        tree[0].Children.Should().HaveCount(1);
        tree[0].Children[0].ItemId.Should().Be(3006);
        tree[0].Children[0].Children.Should().BeEmpty();
    }

    private static ChampionPatternAggregate BuildAggregate(IReadOnlyList<int> buildItems, int games, int wins)
    {
        var build = buildItems.Concat(Enumerable.Repeat(0, 7)).Take(7).ToArray();

        return new ChampionPatternAggregate
        {
            RiotAccountId = Guid.NewGuid(),
            ChampionId = 22,
            GameVersion = "16.5",
            PlatformId = "KR",
            QueueId = 420,
            Position = "BOTTOM",
            PrimaryStyleId = 8000,
            SubStyleId = 8100,
            PerksOffense = 5005,
            PerksFlex = 5008,
            PerksDefense = 5002,
            SummonerSpell1Id = 4,
            SummonerSpell2Id = 7,
            SkillOrderKey = "Q-W-E",
            StarterItems = [1055, 2003],
            StarterItemsKey = "1055-2003",
            BuildItem0 = build[0],
            BuildItem1 = build[1],
            BuildItem2 = build[2],
            BuildItem3 = build[3],
            BuildItem4 = build[4],
            BuildItem5 = build[5],
            BuildItem6 = build[6],
            Games = games,
            Wins = wins,
            LastGameStartTimeUtc = DateTime.UtcNow,
            AggregatedAtUtc = DateTime.UtcNow
        };
    }
}
