using AwesomeAssertions;
using Data.Entities;
using Data.ItemContext;
using Ingestor.Options;
using Ingestor.Processes.Components.ItemContextAggregation;

namespace TrueMain.UnitTests;

/// <summary>
/// The next-item model of #1749 end to end on hand-sized counters: the terms the builder
/// derives from the item-context counters, the way the scorer combines them for one game,
/// and how a game's completed items place it on the build tree.
/// </summary>
public sealed class NextItemModelTests
{
    private const int Champion = 266;
    private const string Position = "TOP";
    private const string Patch = "16.4";
    private const string Previous = "16.3";

    private const int Root = 0;
    private const int Armor = 3143;
    private const int Health = 3083;
    private const int MagicResist = 4401;
    private const int Ninja = 3047;
    private const int Mercs = 3111;

    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ItemContextScope Scope = new(Champion, Position, Patch);
    private static readonly IReadOnlyDictionary<ItemContextAxis, ItemContextBucket> NoSituation =
        new Dictionary<ItemContextAxis, ItemContextBucket>();

    [Fact]
    public void TheBaseTermIsTheLogShareOfTheBranch_OnTheServedPatch()
    {
        var terms = Build(
            [Stat(Root, Armor, 600), Stat(Root, MagicResist, 400)],
            [Total(Root, 1000)]);

        Base(terms, Armor).Weight.Should().BeApproximately(Math.Log(0.6), 1e-9);
        Base(terms, Armor).BranchGames.Should().Be(1000);
        Base(terms, Armor).PatchWindow.Should().Be(1);
    }

    [Fact]
    public void AThinServedBranchWidensItsBaseBackwards_UntilItHoldsTheFloor()
    {
        var terms = Build(
            [Stat(Root, Armor, 10), Stat(Root, Armor, 300, patch: Previous), Stat(Root, MagicResist, 100, patch: Previous)],
            [Total(Root, 10), Total(Root, 400, patch: Previous)],
            window: [Patch, Previous]);

        Base(terms, Armor).PatchWindow.Should().Be(2);
        Base(terms, Armor).Weight.Should().BeApproximately(Math.Log(310 / 410d), 1e-9);
        terms.Should().NotContain(term => term.ItemId == MagicResist,
            "an item only the older patch built is not a decision of the served patch");
    }

    [Fact]
    public void AnItemUnderTheCandidateFloorGetsNoTermAtAll()
    {
        var terms = Build(
            [Stat(Root, Armor, 990), Stat(Root, MagicResist, 10)],
            [Total(Root, 1000)]);

        terms.Should().NotContain(term => term.ItemId == MagicResist);
    }

    [Fact]
    public void ABucketShiftIsTheLogRatioOfItsShrunkShareToTheOverallShare()
    {
        // 400 of the 500 magic-heavy games took the MR item, against 40% overall: the bucket
        // share is shrunk towards 0.4 by 50 pseudo-games before the ratio is taken.
        var terms = Build(
            [
                Stat(Root, MagicResist, 400), Stat(Root, Armor, 600),
                Stat(Root, MagicResist, 400, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High),
                Stat(Root, Armor, 100, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High),
            ],
            [Total(Root, 1000), Total(Root, 500, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High)]);

        var expected = Math.Log(((400 + (50 * 0.4)) / (500 + 50d)) / 0.4);
        Lift(terms, MagicResist, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High).Weight
            .Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void AnEligibleItemThatNeverAppearsInABucketGetsAMeasuredNegativeShift()
    {
        // The armour item answers magic damage in neither direction here, but it is eligible
        // for the axis (it has rows on it), and in the Low bucket the MR item has none: zero
        // games is a measurement, not a missing value.
        var terms = Build(
            [
                Stat(Root, MagicResist, 400), Stat(Root, Armor, 600),
                Stat(Root, MagicResist, 400, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High),
                Stat(Root, Armor, 300, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low),
            ],
            [
                Total(Root, 1000),
                Total(Root, 500, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High),
                Total(Root, 300, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low),
            ]);

        Lift(terms, MagicResist, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.Low).Weight.Should().BeNegative();
    }

    [Fact]
    public void AThinBucketBarelyMovesAnItem_WhereADeepOneMovesItByWhatItMeasured()
    {
        var thin = Lift(
            Build(
                [Stat(Root, MagicResist, 400), Stat(Root, Armor, 600), Stat(Root, MagicResist, 8, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High)],
                [Total(Root, 1000), Total(Root, 10, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High)]),
            MagicResist, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High);
        var deep = Lift(
            Build(
                [Stat(Root, MagicResist, 400), Stat(Root, Armor, 600), Stat(Root, MagicResist, 800, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High)],
                [Total(Root, 1000), Total(Root, 1000, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High)]),
            MagicResist, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High);

        thin.Weight.Should().BeLessThan(0.2, "eight games out of ten measure twice the share, but next to fifty pseudo-games of prior they are noise");
        deep.Weight.Should().BeGreaterThan(Math.Log(1.8));
    }

    [Fact]
    public void ScoresAreTheBaseSharesWhenTheGameSitsInNoSituation()
    {
        var model = Model(
            [Stat(Root, Armor, 600), Stat(Root, MagicResist, 400)],
            [Total(Root, 1000)]);

        var scores = NextItemScorer.Score(model.Branch(ItemContextSlot.Build, Root)!, NoSituation, new HashSet<int>());

        scores.Select(score => score.ItemId).Should().Equal(Armor, MagicResist);
        scores[0].Share.Should().BeApproximately(0.6, 1e-9);
        scores[0].Share.Should().BeApproximately(scores[0].BaseShare, 1e-9);
    }

    [Fact]
    public void ASituationTheCohortReactsToReordersTheCandidates()
    {
        var model = Model(
            [
                Stat(Root, MagicResist, 400), Stat(Root, Armor, 600),
                Stat(Root, MagicResist, 400, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High),
                Stat(Root, Armor, 100, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High),
            ],
            [Total(Root, 1000), Total(Root, 500, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High)]);

        var scores = NextItemScorer.Score(
            model.Branch(ItemContextSlot.Build, Root)!,
            new Dictionary<ItemContextAxis, ItemContextBucket> { [ItemContextAxis.EnemyMagicDamage] = ItemContextBucket.High },
            new HashSet<int>());

        scores[0].ItemId.Should().Be(MagicResist);
        scores[0].Contributions.Should().ContainSingle()
            .Which.Axis.Should().Be(ItemContextAxis.EnemyMagicDamage);
        scores.Sum(score => score.Share).Should().BeApproximately(1, 1e-9);
    }

    [Fact]
    public void EverySituationTheGameSitsInAddsItsShift()
    {
        var model = Model(
            [
                Stat(Root, MagicResist, 400), Stat(Root, Armor, 600),
                Stat(Root, MagicResist, 350, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High),
                Stat(Root, MagicResist, 300, ItemContextAxis.OpponentMagicDamage, ItemContextBucket.High),
            ],
            [
                Total(Root, 1000),
                Total(Root, 500, ItemContextAxis.EnemyMagicDamage, ItemContextBucket.High),
                Total(Root, 500, ItemContextAxis.OpponentMagicDamage, ItemContextBucket.High),
            ]);
        var branch = model.Branch(ItemContextSlot.Build, Root)!;

        var one = NextItemScorer.Score(
            branch,
            new Dictionary<ItemContextAxis, ItemContextBucket> { [ItemContextAxis.EnemyMagicDamage] = ItemContextBucket.High },
            new HashSet<int>()).Single(score => score.ItemId == MagicResist);
        var both = NextItemScorer.Score(
            branch,
            new Dictionary<ItemContextAxis, ItemContextBucket>
            {
                [ItemContextAxis.EnemyMagicDamage] = ItemContextBucket.High,
                [ItemContextAxis.OpponentMagicDamage] = ItemContextBucket.High,
            },
            new HashSet<int>()).Single(score => score.ItemId == MagicResist);

        both.Contributions.Select(c => c.Axis).Should().Equal(ItemContextAxis.EnemyMagicDamage, ItemContextAxis.OpponentMagicDamage);
        both.Share.Should().BeGreaterThan(one.Share);
    }

    [Fact]
    public void AnOwnedItemIsNeverACandidate_AndTheOthersShareTheBranchBetweenThem()
    {
        var model = Model(
            [Stat(Root, Armor, 500), Stat(Root, MagicResist, 300), Stat(Root, Health, 200)],
            [Total(Root, 1000)]);

        var scores = NextItemScorer.Score(model.Branch(ItemContextSlot.Build, Root)!, NoSituation, new HashSet<int> { Armor });

        scores.Select(score => score.ItemId).Should().Equal(MagicResist, Health);
        scores[0].Share.Should().BeApproximately(0.6, 1e-9);
    }

    [Fact]
    public void ABuildOnTheTreeWalksItEdgeByEdge()
    {
        var model = Model(
            [Stat(Root, Armor, 600), Stat(Root, MagicResist, 400), Stat(Armor, Health, 500), Stat(Health, MagicResist, 300)],
            [Total(Root, 1000), Total(Armor, 500), Total(Health, 300)]);

        var (path, onTree) = model.ResolveBuildPath([1036, Armor, 1001, Health]);

        path.Should().Equal(Root, Armor, Health);
        onTree.Should().BeTrue();
    }

    [Fact]
    public void ABuildThatLeavesTheTreeReanchorsOnItsLatestItemThatIsABranch()
    {
        // Health is never a first item here; a game that opens on it is off the tree, but it
        // is a branch of its own (after armour), so the game continues from there rather than
        // being sent back to the root.
        var model = Model(
            [Stat(Root, Armor, 600), Stat(Root, MagicResist, 400), Stat(Armor, Health, 500), Stat(Health, MagicResist, 300)],
            [Total(Root, 1000), Total(Armor, 500), Total(Health, 300)]);

        var (path, onTree) = model.ResolveBuildPath([Health]);

        path[^1].Should().Be(Health);
        onTree.Should().BeFalse();
    }

    [Fact]
    public void BootsAreTheirOwnBranch_AndKnownAsBoots()
    {
        var model = Model(
            [Stat(Root, Ninja, 700, slot: ItemContextSlot.Boots), Stat(Root, Mercs, 300, slot: ItemContextSlot.Boots), Stat(Root, Armor, 1000)],
            [Total(Root, 1000, slot: ItemContextSlot.Boots), Total(Root, 1000)]);

        model.IsBoots(Ninja).Should().BeTrue();
        model.IsBoots(Armor).Should().BeFalse();
        model.IsBuildItem(Ninja).Should().BeFalse();
    }

    [Fact]
    public void StartersAreNotModelled_TheDraftAlreadyShowsThem()
    {
        var terms = Build(
            [Stat(Root, 1054, 800, slot: ItemContextSlot.Starter)],
            [Total(Root, 1000, slot: ItemContextSlot.Starter)]);

        terms.Should().BeEmpty();
    }

    private static NextItemModel Model(ChampionItemContextStat[] stats, ChampionItemContextTotal[] totals)
        => NextItemModel.From(Build(stats, totals));

    private static IReadOnlyList<ChampionNextItemTerm> Build(
        ChampionItemContextStat[] stats,
        ChampionItemContextTotal[] totals,
        string[]? window = null)
        => NextItemTermBuilder.Build(Scope, stats, totals, window ?? [Patch], new NextItemModelOptions(), Now);

    private static ChampionNextItemTerm Base(IReadOnlyList<ChampionNextItemTerm> terms, int itemId)
        => terms.Single(term => term.ItemId == itemId && term.Axis == ItemContextAxis.Overall);

    private static ChampionNextItemTerm Lift(
        IReadOnlyList<ChampionNextItemTerm> terms, int itemId, ItemContextAxis axis, ItemContextBucket bucket)
        => terms.Single(term => term.ItemId == itemId && term.Axis == axis && term.Bucket == bucket);

    private static ChampionItemContextStat Stat(
        int parent,
        int item,
        int games,
        ItemContextAxis axis = ItemContextAxis.Overall,
        ItemContextBucket bucket = ItemContextBucket.All,
        string patch = Patch,
        ItemContextSlot slot = ItemContextSlot.Build)
        => new()
        {
            ChampionId = Champion,
            Position = Position,
            Patch = patch,
            Slot = slot,
            ParentItemId = parent,
            ItemId = item,
            Axis = axis,
            Bucket = bucket,
            Games = games,
            Wins = games / 2,
        };

    private static ChampionItemContextTotal Total(
        int parent,
        int games,
        ItemContextAxis axis = ItemContextAxis.Overall,
        ItemContextBucket bucket = ItemContextBucket.All,
        string patch = Patch,
        ItemContextSlot slot = ItemContextSlot.Build)
        => new()
        {
            ChampionId = Champion,
            Position = Position,
            Patch = patch,
            Slot = slot,
            ParentItemId = parent,
            Axis = axis,
            Bucket = bucket,
            Games = games,
            Wins = games / 2,
        };
}
