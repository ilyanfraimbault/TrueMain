using AwesomeAssertions;
using TrueMain.Services.Champions;

namespace TrueMain.UnitTests;

public sealed class CompositionBuildAggregatorTests
{
    private const double WinWeight = 2d;
    private const int SituationalCount = 5;

    private static readonly CompositionRunePageFacts ConquerorPage =
        new(8000, 8010, 9111, 9104, 8014, 8100, 8139, 8135, 5005, 5008, 5001);

    private static readonly CompositionRunePageFacts ElectrocutePage =
        new(8100, 8112, 8139, 8138, 8135, 8000, 9111, 8014, 5008, 5008, 5001);

    private static CompositionParticipantFacts Facts(
        bool win,
        int[]? buildItems = null,
        int bootsItemId = 0,
        int[]? starterItems = null,
        int spell1 = 0,
        int spell2 = 0,
        string skillOrderKey = "",
        CompositionRunePageFacts? runePage = null,
        double similarityWeight = 1d)
        => new()
        {
            Win = win,
            SimilarityWeight = similarityWeight,
            BuildItems = buildItems ?? [],
            BootsItemId = bootsItemId,
            StarterItems = starterItems ?? [],
            Spell1Id = spell1,
            Spell2Id = spell2,
            SkillOrderKey = skillOrderKey,
            RunePage = runePage,
        };

    [Fact]
    public void Aggregate_EmptyTopK_ReturnsEmptyRecommendation()
    {
        var result = CompositionBuildAggregator.Aggregate([], WinWeight, SituationalCount);

        result.GamesConsidered.Should().Be(0);
        result.RunePage.Should().BeNull();
        result.StarterItems.Should().BeNull();
        result.Boots.Should().BeNull();
        result.CorePath.Should().BeNull();
        result.SituationalItems.Should().BeEmpty();
        result.SummonerSpells.Should().BeNull();
        result.SkillOrder.Should().BeNull();
    }

    [Fact]
    public void Aggregate_WinWeighting_LetsAWinningMinorityOutvoteALosingMajority()
    {
        // Three losses on Q-W-E (weight 3) vs two wins on Q-E-W (weight 4):
        // the winning minority must take the recommendation.
        var facts = new[]
        {
            Facts(win: false, skillOrderKey: "Q-W-E"),
            Facts(win: false, skillOrderKey: "Q-W-E"),
            Facts(win: false, skillOrderKey: "Q-W-E"),
            Facts(win: true, skillOrderKey: "Q-E-W"),
            Facts(win: true, skillOrderKey: "Q-E-W"),
        };

        var result = CompositionBuildAggregator.Aggregate(facts, WinWeight, SituationalCount);

        result.SkillOrder.Should().NotBeNull();
        result.SkillOrder!.Sequence.Should().Equal("Q", "E", "W");
        // Reported numbers stay raw: 2 of 5 games, both won.
        result.SkillOrder.Games.Should().Be(2);
        result.SkillOrder.PickRate.Should().BeApproximately(2d / 5d, 1e-9);
        result.SkillOrder.WinRate.Should().Be(1d);
    }

    [Fact]
    public void Aggregate_SimilarityWeighting_LetsTheClosestGamesOutvoteTheMajority()
    {
        // Three barely-similar losses on Q-W-E (weight 3×1) vs one highly
        // similar loss on Q-E-W (weight 1×4): the closest game must win the
        // vote even though it is a 1-vs-3 minority with no win bonus.
        var facts = new[]
        {
            Facts(win: false, skillOrderKey: "Q-W-E"),
            Facts(win: false, skillOrderKey: "Q-W-E"),
            Facts(win: false, skillOrderKey: "Q-W-E"),
            Facts(win: false, skillOrderKey: "Q-E-W", similarityWeight: 4d),
        };

        var result = CompositionBuildAggregator.Aggregate(facts, WinWeight, SituationalCount);

        result.SkillOrder.Should().NotBeNull();
        result.SkillOrder!.Sequence.Should().Equal("Q", "E", "W");
        // Reported numbers stay raw counts regardless of the weights.
        result.SkillOrder.Games.Should().Be(1);
        result.SkillOrder.PickRate.Should().BeApproximately(1d / 4d, 1e-9);
    }

    [Fact]
    public void Aggregate_SimilarityWeighting_ScalesSituationalItemVotes()
    {
        // Item 3036 appears in two barely-similar games (weight 2), item 3033
        // in one highly similar game (weight 4) — 3033 must rank first.
        var facts = new[]
        {
            Facts(win: false, buildItems: [3031, 3153, 3072, 3036]),
            Facts(win: false, buildItems: [3031, 3153, 3072, 3036]),
            Facts(win: false, buildItems: [3031, 3153, 3072, 3033], similarityWeight: 4d),
        };

        var result = CompositionBuildAggregator.Aggregate(facts, winWeight: 1d, SituationalCount);

        result.SituationalItems.Should().HaveCount(2);
        result.SituationalItems[0].ItemIds.Should().Equal(3033);
        result.SituationalItems[1].ItemIds.Should().Equal(3036);
    }

    [Fact]
    public void Aggregate_BuildTree_RootsOnTheCorePathFirstItemWithRawCounts()
    {
        var facts = new[]
        {
            Facts(win: true, buildItems: [3031, 3153, 3036]),
            Facts(win: true, buildItems: [3031, 3153, 3072]),
            Facts(win: false, buildItems: [3031, 3153, 3036]),
            // Different opening — outside the tree rooted on 3031.
            Facts(win: false, buildItems: [3072, 3026]),
        };

        var result = CompositionBuildAggregator.Aggregate(facts, WinWeight, SituationalCount);

        result.FirstItemId.Should().Be(3031);
        result.BuildTree.Should().HaveCount(1);
        var second = result.BuildTree[0];
        second.ItemId.Should().Be(3153);
        second.Games.Should().Be(3);
        second.Wins.Should().Be(2);
        second.PickRate.Should().Be(1d);
        // 3036 (2 games) survives the prune; the 1-game 3072 branch is below
        // the tree's minimum support and drops out.
        second.Children.Select(c => c.ItemId).Should().Equal(3036);
    }

    [Fact]
    public void Aggregate_UnitWinWeight_FallsBackToPlainMajority()
    {
        var facts = new[]
        {
            Facts(win: false, skillOrderKey: "Q-W-E"),
            Facts(win: false, skillOrderKey: "Q-W-E"),
            Facts(win: false, skillOrderKey: "Q-W-E"),
            Facts(win: true, skillOrderKey: "Q-E-W"),
            Facts(win: true, skillOrderKey: "Q-E-W"),
        };

        var result = CompositionBuildAggregator.Aggregate(facts, winWeight: 1d, SituationalCount);

        result.SkillOrder!.Sequence.Should().Equal("Q", "W", "E");
    }

    [Fact]
    public void Aggregate_SparseFacts_AbstainPerDimensionWithoutThrowing()
    {
        // Games missing timeline/rune data abstain on those dimensions; the
        // ones that do have data still elect a recommendation.
        var facts = new[]
        {
            Facts(win: true, spell1: 4, spell2: 12),
            Facts(win: false, spell1: 4, spell2: 12, skillOrderKey: "Q-W-E"),
        };

        var result = CompositionBuildAggregator.Aggregate(facts, WinWeight, SituationalCount);

        result.GamesConsidered.Should().Be(2);
        result.Wins.Should().Be(1);
        result.RunePage.Should().BeNull("no game carried rune selections");
        result.StarterItems.Should().BeNull();
        result.Boots.Should().BeNull();
        result.CorePath.Should().BeNull();
        result.SituationalItems.Should().BeEmpty();
        result.SummonerSpells.Should().NotBeNull();
        result.SummonerSpells!.Spell1Id.Should().Be(4);
        result.SummonerSpells.Spell2Id.Should().Be(12);
        result.SummonerSpells.Games.Should().Be(2);
        result.SkillOrder.Should().NotBeNull();
        result.SkillOrder!.Games.Should().Be(1, "only one game had skill events");
    }

    [Fact]
    public void Aggregate_CorePath_GroupsOnTheFirstThreeCompletedItems()
    {
        // Same three first legendaries, diverging afterwards → one core path
        // supported by both games.
        var facts = new[]
        {
            Facts(win: true, buildItems: [3031, 3153, 3072, 3036]),
            Facts(win: false, buildItems: [3031, 3153, 3072, 3026]),
            Facts(win: false, buildItems: [3153, 3031, 3072]),
        };

        var result = CompositionBuildAggregator.Aggregate(facts, WinWeight, SituationalCount);

        result.CorePath.Should().NotBeNull();
        result.CorePath!.ItemIds.Should().Equal(3031, 3153, 3072);
        result.CorePath.Games.Should().Be(2);
        result.CorePath.WinRate.Should().BeApproximately(0.5d, 1e-9);
    }

    [Fact]
    public void Aggregate_ShortBuild_StillFormsACorePath()
    {
        var facts = new[]
        {
            Facts(win: true, buildItems: [3031]),
            Facts(win: true, buildItems: [3031]),
        };

        var result = CompositionBuildAggregator.Aggregate(facts, WinWeight, SituationalCount);

        result.CorePath!.ItemIds.Should().Equal(3031);
        result.CorePath.Games.Should().Be(2);
    }

    [Fact]
    public void Aggregate_SituationalItems_ExcludeTheCorePathAndVoteOncePerGame()
    {
        // Core path = [3031, 3153, 3072] (both games). Beyond it: 3036 appears
        // in a winning game (weight 2), 3026 in a losing one (weight 1) — and
        // 3036 appearing twice in one build must still count as a single vote.
        var facts = new[]
        {
            Facts(win: true, buildItems: [3031, 3153, 3072, 3036, 3036]),
            Facts(win: false, buildItems: [3031, 3153, 3072, 3026]),
        };

        var result = CompositionBuildAggregator.Aggregate(facts, WinWeight, SituationalCount);

        result.SituationalItems.Should().HaveCount(2);
        result.SituationalItems[0].ItemIds.Should().Equal(3036);
        result.SituationalItems[0].Games.Should().Be(1);
        result.SituationalItems[0].WinRate.Should().Be(1d);
        result.SituationalItems[1].ItemIds.Should().Equal(3026);
        result.SituationalItems.Should().NotContain(s => s.ItemIds.Contains(3031));
    }

    [Fact]
    public void Aggregate_SituationalItems_AreCappedToTheConfiguredCount()
    {
        var facts = new[]
        {
            Facts(win: true, buildItems: [3031, 3153, 3072, 3036, 3026, 3142, 3814]),
        };

        var result = CompositionBuildAggregator.Aggregate(facts, WinWeight, situationalItemCount: 2);

        result.SituationalItems.Should().HaveCount(2);
    }

    [Fact]
    public void Aggregate_RunePages_GroupByValueAndWinWeightTheVote()
    {
        var facts = new[]
        {
            Facts(win: false, runePage: ConquerorPage),
            Facts(win: false, runePage: ConquerorPage),
            Facts(win: false, runePage: ConquerorPage),
            Facts(win: true, runePage: ElectrocutePage),
            Facts(win: true, runePage: ElectrocutePage),
        };

        var result = CompositionBuildAggregator.Aggregate(facts, WinWeight, SituationalCount);

        result.RunePage.Should().NotBeNull();
        result.RunePage!.PrimaryKeystoneId.Should().Be(
            ElectrocutePage.PrimaryKeystoneId, "2 wins × 2 outweigh 3 losses × 1");
        result.RunePage.Games.Should().Be(2);
        result.RunePage.WinRate.Should().Be(1d);
        result.RunePage.StatOffense.Should().Be(ElectrocutePage.StatOffense);
    }

    [Fact]
    public void Aggregate_StarterBootsAndSpells_ElectTheWeightedTop()
    {
        var facts = new[]
        {
            Facts(win: false, bootsItemId: 3006, starterItems: [1055, 2003], spell1: 4, spell2: 12),
            Facts(win: false, bootsItemId: 3006, starterItems: [1055, 2003], spell1: 4, spell2: 12),
            Facts(win: true, bootsItemId: 3047, starterItems: [1054], spell1: 4, spell2: 14),
        };

        var result = CompositionBuildAggregator.Aggregate(facts, WinWeight, SituationalCount);

        // 2 losses (weight 2) tie the single win (weight 2); raw games break
        // the tie toward the majority.
        result.Boots!.ItemIds.Should().Equal(3006);
        result.Boots.Games.Should().Be(2);
        result.StarterItems!.ItemIds.Should().Equal(1055, 2003);
        result.SummonerSpells!.Spell2Id.Should().Be(12);
    }
}
