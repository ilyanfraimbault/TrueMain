using System.Collections.Frozen;
using AwesomeAssertions;
using Data.BuildFacts;
using Data.Entities;
using Data.ItemContext;
using Data.Statics;
using Ingestor.Processes.Components.ItemContextAggregation;

namespace TrueMain.UnitTests;

/// <summary>
/// The in-game item-context axes of #1750, piece by piece: which items count as built, how an
/// enemy's timeline is replayed to the instant a decision opened, how what was built is banded,
/// and how the fold hands each branch its own reading.
/// </summary>
public sealed class BuiltItemContextAxesTests
{
    private const int Thornmail = 3075;
    private const int ClothArmor = 1029;
    private const int Steelcaps = 3047;
    private const int SpiritVisage = 3065;
    private const int Bloodthirster = 3072;
    private const int InfinityEdge = 3031;
    private const int Rabadon = 3089;
    private const int Riftmaker = 4633;
    private const int DoransBlade = 1055;

    private static readonly BuiltItemCatalog Catalog = BuiltItemCatalog.From(Metadata(), Stats());

    [Fact]
    public void TheCatalogueHoldsCompletedItemsAndBoots_NeverComponentsOrStarters()
    {
        Catalog.TryGet(Thornmail, out var thornmail).Should().BeTrue();
        thornmail.Armor.Should().Be(75);
        Catalog.TryGet(Steelcaps, out var steelcaps).Should().BeTrue("completed boots are armour a player has to go through");
        steelcaps.Armor.Should().Be(25);

        Catalog.TryGet(ClothArmor, out _).Should().BeFalse("a component is a step towards an item, not an item");
        Catalog.TryGet(DoransBlade, out _).Should().BeFalse("a starter is not a build");
    }

    [Fact]
    public void TheCatalogueReadsDamageTypeCritAndHealingOffTheStats()
    {
        Catalog.TryGet(Rabadon, out var rabadon).Should().BeTrue();
        rabadon.Damage.Should().Be(BuiltDamage.Magic);

        Catalog.TryGet(InfinityEdge, out var infinityEdge).Should().BeTrue();
        infinityEdge.Damage.Should().Be(BuiltDamage.Physical);
        infinityEdge.Crit.Should().BeTrue();

        Catalog.TryGet(Bloodthirster, out var bloodthirster).Should().BeTrue();
        bloodthirster.Healing.Should().BeTrue("life steal");

        Catalog.TryGet(Riftmaker, out var riftmaker).Should().BeTrue();
        riftmaker.Healing.Should().BeTrue("omnivamp has no stat key, only Data Dragon's SpellVamp tag");
        riftmaker.Damage.Should().Be(BuiltDamage.Magic);

        Catalog.TryGet(Thornmail, out var thornmail).Should().BeTrue();
        thornmail.Damage.Should().Be(BuiltDamage.None);
    }

    [Fact]
    public void AnItemCountsFromTheEventThatCompletedIt_NotOneComponentEarlier()
    {
        List<ItemEvent> events =
        [
            Bought(ClothArmor, 300_000),
            Bought(ClothArmor, 420_000),
            Destroyed(ClothArmor, 500_000),
            Destroyed(ClothArmor, 500_000),
            Bought(Thornmail, 500_000),
        ];

        BuiltInventoryReplay.At(events, 450_000, Catalog).Should().BeEmpty();
        BuiltInventoryReplay.At(events, 500_000, Catalog).Should().Equal(Thornmail);
    }

    [Fact]
    public void ASaleOrADestructionTakesTheItemBack()
    {
        BuiltInventoryReplay.At([Bought(Thornmail, 500_000), Sold(Thornmail, 900_000)], 1_000_000, Catalog)
            .Should().BeEmpty();
        BuiltInventoryReplay.At([Bought(Thornmail, 500_000), Destroyed(Thornmail, 900_000)], 1_000_000, Catalog)
            .Should().BeEmpty();
        BuiltInventoryReplay.At([Bought(Thornmail, 500_000), Sold(Thornmail, 900_000)], 800_000, Catalog)
            .Should().Equal(Thornmail);
    }

    [Fact]
    public void AnUndoTakesBackAPurchaseAndRestoresASale()
    {
        BuiltInventoryReplay.At([Bought(Thornmail, 500_000), Undo(before: Thornmail, after: 0, 505_000)], 600_000, Catalog)
            .Should().BeEmpty("an undone purchase was never built");

        BuiltInventoryReplay.At(
                [Bought(Thornmail, 500_000), Sold(Thornmail, 900_000), Undo(before: 0, after: Thornmail, 905_000)],
                1_000_000,
                Catalog)
            .Should().Equal(Thornmail);
    }

    [Fact]
    public void SeveralInstantsAreReadInOnePass_InTheOrderAsked()
    {
        List<ItemEvent> events = [Bought(Thornmail, 500_000), Bought(SpiritVisage, 900_000), Sold(Thornmail, 1_100_000)];

        var snapshots = BuiltInventoryReplay.At(events, [1_200_000, 400_000, 1_000_000], Catalog);

        snapshots[0].Should().Equal(SpiritVisage);
        snapshots[1].Should().BeEmpty();
        snapshots[2].Should().Equal(Thornmail, SpiritVisage);
    }

    [Fact]
    public void ResistancesAreSummedOverTheWholeTeam_AndBandedAtTheirEdges()
    {
        var thresholds = new BuiltAxisThresholds { EnemyArmorLow = 40, EnemyArmorHigh = 100 };

        Team(thresholds, [Steelcaps])[ItemContextAxis.EnemyArmorBuilt].Should().Be(ItemContextBucket.Low);
        Team(thresholds, [Thornmail])[ItemContextAxis.EnemyArmorBuilt].Should().Be(ItemContextBucket.Mid);
        Team(thresholds, [Thornmail], [Steelcaps])[ItemContextAxis.EnemyArmorBuilt].Should().Be(ItemContextBucket.High);
        Team(thresholds, [SpiritVisage])[ItemContextAxis.EnemyMagicResistBuilt].Should().Be(ItemContextBucket.Mid);
    }

    [Fact]
    public void HealingCountsEnemies_CritCountsItems()
    {
        var thresholds = new BuiltAxisThresholds { EnemyHealingLow = 0, EnemyHealingHigh = 2, EnemyCritLow = 0, EnemyCritHigh = 2 };

        Team(thresholds, [Bloodthirster, Riftmaker])[ItemContextAxis.EnemyHealingBuilt]
            .Should().Be(ItemContextBucket.Mid, "one enemy holding two healing items is one enemy that heals");
        Team(thresholds, [Bloodthirster], [Riftmaker])[ItemContextAxis.EnemyHealingBuilt].Should().Be(ItemContextBucket.High);

        Team(thresholds, [InfinityEdge])[ItemContextAxis.EnemyCritBuilt].Should().Be(ItemContextBucket.Mid);
        Team(thresholds, [InfinityEdge, InfinityEdge])[ItemContextAxis.EnemyCritBuilt].Should().Be(ItemContextBucket.High);
        Team(thresholds)[ItemContextAxis.EnemyCritBuilt].Should().Be(ItemContextBucket.Low);
    }

    [Fact]
    public void TheDamageBuiltIsAShareOfTheDamageItems_OnlyOnceThereAreEnoughOfThem()
    {
        var thresholds = new BuiltAxisThresholds();

        Team(thresholds, [Rabadon]).Should().NotContainKey(ItemContextAxis.EnemyMagicDamageBuilt, "one item is a coin, not a profile");
        Team(thresholds, [Rabadon], [Riftmaker])[ItemContextAxis.EnemyMagicDamageBuilt].Should().Be(ItemContextBucket.High);
        Team(thresholds, [Bloodthirster], [InfinityEdge], [Rabadon])[ItemContextAxis.EnemyMagicDamageBuilt].Should().Be(ItemContextBucket.Mid);
        Team(thresholds, [Bloodthirster], [InfinityEdge])[ItemContextAxis.EnemyMagicDamageBuilt].Should().Be(ItemContextBucket.Low);
        Team(thresholds, [Thornmail], [SpiritVisage]).Should().NotContainKey(ItemContextAxis.EnemyMagicDamageBuilt);
    }

    [Fact]
    public void AnAxisTheContextCannotCarryIsAbsent_NeverDefaulted()
    {
        var thresholds = new BuiltAxisThresholds();

        var opponentOnly = BuiltAxisEvaluator.Evaluate(new BuiltContext(null, [Thornmail]), Catalog, thresholds);
        opponentOnly.Keys.Should().BeEquivalentTo([ItemContextAxis.OpponentArmorBuilt, ItemContextAxis.OpponentMagicResistBuilt]);
        opponentOnly[ItemContextAxis.OpponentArmorBuilt].Should().Be(ItemContextBucket.High);

        BuiltAxisEvaluator.Evaluate(new BuiltContext(null, null), Catalog, thresholds).Should().BeEmpty();
        Team(thresholds).Should().NotContainKey(ItemContextAxis.OpponentArmorBuilt);
    }

    [Fact]
    public void AnInGameAxisOnlyCountsForTheItemsThatAnswerIt()
    {
        var lethality = Whitelist(ItemContextSlot.Build, "Damage", "ArmorPenetration");
        lethality.Should().Contain([ItemContextAxis.EnemyArmorBuilt, ItemContextAxis.OpponentArmorBuilt]);
        lethality.Should().NotContain([ItemContextAxis.EnemyMagicResistBuilt, ItemContextAxis.EnemyCritBuilt, ItemContextAxis.EnemyHealingBuilt]);

        Whitelist(ItemContextSlot.Build, "SpellDamage", "MagicPenetration")
            .Should().Contain([ItemContextAxis.EnemyMagicResistBuilt, ItemContextAxis.OpponentMagicResistBuilt]);

        Whitelist(ItemContextSlot.Build, "Armor", "Health")
            .Should().Contain([ItemContextAxis.EnemyCritBuilt, ItemContextAxis.EnemyMagicDamageBuilt]);
        Whitelist(ItemContextSlot.Build, "SpellBlock", "Health")
            .Should().Contain(ItemContextAxis.EnemyMagicDamageBuilt)
            .And.NotContain(ItemContextAxis.EnemyCritBuilt);

        var antiHeal = ItemContextWhitelist.For(Item(3033, "Damage") with { GrantsGrievousWounds = true }, ItemContextSlot.Build);
        antiHeal.Should().Contain(ItemContextAxis.EnemyHealingBuilt);

        Whitelist(ItemContextSlot.Build, "SpellDamage").Where(axis => !ItemContextAxes.IsDraftTime(axis))
            .Should().Equal([ItemContextAxis.OwnGoldLeadAt15], "pure ability power answers nothing the enemies built");
    }

    [Fact]
    public void TheRootDecisionsCarryNoInGameAxis()
    {
        foreach (var slot in new[] { ItemContextSlot.Boots, ItemContextSlot.Starter })
        {
            Whitelist(slot, "Armor", "ArmorPenetration", "Boots")
                .Where(axis => ItemContextAxes.IsOverlayOnly(axis))
                .Should().BeEmpty($"the {slot} decision is taken before anybody has built anything");
        }
    }

    [Fact]
    public void TheAxesAreMarkedInGame_AndTheNewOnesStayOffTheSite()
    {
        var built = new[]
        {
            ItemContextAxis.EnemyArmorBuilt, ItemContextAxis.EnemyMagicResistBuilt, ItemContextAxis.OpponentArmorBuilt,
            ItemContextAxis.OpponentMagicResistBuilt, ItemContextAxis.EnemyHealingBuilt, ItemContextAxis.EnemyMagicDamageBuilt,
            ItemContextAxis.EnemyCritBuilt,
        };

        foreach (var axis in built)
        {
            ItemContextAxes.IsDraftTime(axis).Should().BeFalse();
            ItemContextAxes.IsOverlayOnly(axis).Should().BeTrue();
            axis.ToString().Length.Should().BeLessThanOrEqualTo(32, "the column is varchar(32)");
        }

        ItemContextAxes.IsDraftTime(ItemContextAxis.OwnGoldLeadAt15).Should().BeFalse();
        ItemContextAxes.IsOverlayOnly(ItemContextAxis.OwnGoldLeadAt15).Should().BeFalse("the site words the gold lead");
        ItemContextAxes.IsDraftTime(ItemContextAxis.EnemyMagicDamage).Should().BeTrue();

        ItemContextAxes.Situational.Should().Contain(built);
        ItemContextAxes.OnSite.Should().NotContain(built).And.Contain(ItemContextAxis.OwnGoldLeadAt15);
    }

    [Fact]
    public void EachBuildDecisionOpensAtItsParentsCompletion()
    {
        var metadata = Metadata();
        List<ItemEvent> events = [Bought(Bloodthirster, 600_000), Bought(InfinityEdge, 1_200_000), Bought(Thornmail, 1_500_000)];

        var timed = FinalBuildResolver.ResolveTimed(events, [Bloodthirster, InfinityEdge, Thornmail], [], metadata);
        timed.Should().Equal((Bloodthirster, 600_000), (InfinityEdge, 1_200_000), (Thornmail, 1_500_000));

        var edges = ItemContextSlotResolver.Resolve(events, [Bloodthirster, InfinityEdge, Thornmail], metadata)[ItemContextSlot.Build];
        edges.Select(edge => (edge.ParentItemId, edge.DecisionAtMs))
            .Should().Equal((0, 0), (Bloodthirster, 600_000), (InfinityEdge, 1_200_000));
    }

    [Fact]
    public void AnItemTheTimelineNeverShowsHasNoCompletionInstant()
    {
        var timed = FinalBuildResolver.ResolveTimed([Bought(Bloodthirster, 600_000)], [Bloodthirster, InfinityEdge], [], Metadata());

        timed.Should().Equal((Bloodthirster, 600_000), (InfinityEdge, (int?)null));
    }

    [Fact]
    public void EachBranchIsPlacedOnWhatTheEnemiesHeldWhenItOpened_TheRootOnNothing()
    {
        IReadOnlyList<ItemContextEdge> edges =
        [
            new(0, Bloodthirster, new HashSet<ItemContextAxis>(), 0),
            new(Bloodthirster, InfinityEdge, new HashSet<ItemContextAxis>(), 600_000),
            new(InfinityEdge, Thornmail, new HashSet<ItemContextAxis>(), 1_200_000),
            new(Thornmail, Rabadon, new HashSet<ItemContextAxis>(), null),
        ];
        List<ItemEvent> tank = [Bought(Thornmail, 900_000)];
        var thresholds = new BuiltAxisThresholds { EnemyArmorLow = 40, EnemyArmorHigh = 70, OpponentArmorLow = 25, OpponentArmorHigh = 60 };
        var distribution = new BuiltAxisDistribution();

        var axes = BuiltBranchAxes.Resolve(edges, [tank, [], [], [], []], tank, Catalog, thresholds, distribution);

        axes.Keys.Should().BeEquivalentTo([Bloodthirster, InfinityEdge], "the root opens at 0:00 and an unknown instant is not guessed");
        axes[Bloodthirster][ItemContextAxis.EnemyArmorBuilt].Should().Be(ItemContextBucket.Low);
        axes[InfinityEdge][ItemContextAxis.EnemyArmorBuilt].Should().Be(ItemContextBucket.High);
        axes[InfinityEdge][ItemContextAxis.OpponentArmorBuilt].Should().Be(ItemContextBucket.High);

        var armour = distribution.Quantiles()[nameof(ItemContextAxis.EnemyArmorBuilt)];
        armour.Decisions.Should().Be(2);
        armour.P10.Should().Be(0);
        armour.P90.Should().Be(75);
    }

    [Fact]
    public void AnUnreadableTeamCarriesOnlyTheLaneOpponent()
    {
        IReadOnlyList<ItemContextEdge> edges = [new(Bloodthirster, InfinityEdge, new HashSet<ItemContextAxis>(), 600_000)];

        var axes = BuiltBranchAxes.Resolve(edges, null, [Bought(Thornmail, 500_000)], Catalog, new BuiltAxisThresholds(), new BuiltAxisDistribution());

        axes[Bloodthirster].Keys.Should().BeEquivalentTo([ItemContextAxis.OpponentArmorBuilt, ItemContextAxis.OpponentMagicResistBuilt]);
    }

    [Fact]
    public void ABranchsInGameReadingCountsOnThatBranchAlone()
    {
        var scope = new ItemContextScope(266, "TOP", "16.20");
        var answers = new HashSet<ItemContextAxis> { ItemContextAxis.EnemyArmorBuilt, ItemContextAxis.EnemyMagicDamage };
        IReadOnlyList<ItemContextEdge> edges =
        [
            new(0, Bloodthirster, answers, 0),
            new(Bloodthirster, InfinityEdge, answers, 600_000),
        ];
        var accumulator = new ItemContextAccumulator();

        accumulator.Add(
            scope,
            ItemContextSlot.Build,
            edges,
            new Dictionary<ItemContextAxis, ItemContextBucket> { [ItemContextAxis.EnemyMagicDamage] = ItemContextBucket.High },
            win: true,
            branchAxes: new Dictionary<int, IReadOnlyDictionary<ItemContextAxis, ItemContextBucket>>
            {
                [Bloodthirster] = new Dictionary<ItemContextAxis, ItemContextBucket> { [ItemContextAxis.EnemyArmorBuilt] = ItemContextBucket.High },
            });

        accumulator.Totals.Keys.Where(key => key.Axis == ItemContextAxis.EnemyArmorBuilt)
            .Should().ContainSingle().Which.ParentItemId.Should().Be(Bloodthirster);
        accumulator.Stats.Keys.Where(key => key.Axis == ItemContextAxis.EnemyArmorBuilt)
            .Should().ContainSingle().Which.ItemId.Should().Be(InfinityEdge);
        accumulator.Totals.Keys.Count(key => key.Axis == ItemContextAxis.EnemyMagicDamage)
            .Should().Be(2, "a draft axis is the same on every branch of the game");
    }

    [Fact]
    public void TheQuantilesAreReadOffTheHistogram()
    {
        var distribution = new BuiltAxisDistribution();
        for (var value = 1; value <= 100; value++)
        {
            distribution.Add([(ItemContextAxis.EnemyArmorBuilt, value)]);
        }

        var quantiles = distribution.Quantiles()[nameof(ItemContextAxis.EnemyArmorBuilt)];

        quantiles.Should().Be(new BuiltAxisQuantiles(100, 10, 25, 50, 75, 90));
    }

    private static IReadOnlyDictionary<ItemContextAxis, ItemContextBucket> Team(BuiltAxisThresholds thresholds, params int[][] members)
    {
        var enemies = members.Select(items => (IReadOnlyList<int>)items).ToList();
        while (enemies.Count < 5)
        {
            enemies.Add([]);
        }

        return BuiltAxisEvaluator.Evaluate(new BuiltContext(enemies, null), Catalog, thresholds);
    }

    private static IReadOnlySet<ItemContextAxis> Whitelist(ItemContextSlot slot, params string[] categories)
        => ItemContextWhitelist.For(Item(9001, categories), slot);

    private static ItemMetadata Item(int id, params string[] categories)
        => new(id, 3000, true, false, categories.Contains("Boots"), false, true, categories.Contains("Boots"))
        {
            Categories = categories.ToFrozenSet(StringComparer.Ordinal),
        };

    private static Dictionary<int, ItemMetadata> Metadata()
    {
        static ItemMetadata Final(int id, bool boots = false) => new(id, 3000, true, false, boots, false, true, boots);

        return new Dictionary<int, ItemMetadata>
        {
            [Thornmail] = Final(Thornmail),
            [SpiritVisage] = Final(SpiritVisage),
            [Bloodthirster] = Final(Bloodthirster),
            [InfinityEdge] = Final(InfinityEdge),
            [Rabadon] = Final(Rabadon),
            [Riftmaker] = Final(Riftmaker),
            [Steelcaps] = Final(Steelcaps, boots: true),
            [ClothArmor] = new(ClothArmor, 300, true, false, false, false, false, false),
            [DoransBlade] = new(DoransBlade, 450, true, false, false, false, true, false) { IsStarterClassItem = true },
        };
    }

    private static Dictionary<int, ItemStats> Stats()
    {
        static ItemStats Of(int id, double armor = 0, double mr = 0, double ad = 0, double ap = 0, double crit = 0, double lifeSteal = 0, params string[] tags)
            => new(id, armor, mr, ad, ap, crit, 0, lifeSteal, tags.ToFrozenSet(StringComparer.Ordinal));

        return new Dictionary<int, ItemStats>
        {
            [Thornmail] = Of(Thornmail, armor: 75),
            [ClothArmor] = Of(ClothArmor, armor: 15),
            [Steelcaps] = Of(Steelcaps, armor: 25),
            [SpiritVisage] = Of(SpiritVisage, mr: 50),
            [Bloodthirster] = Of(Bloodthirster, ad: 80, lifeSteal: 0.15),
            [InfinityEdge] = Of(InfinityEdge, ad: 75, crit: 0.25),
            [Rabadon] = Of(Rabadon, ap: 130),
            [Riftmaker] = Of(Riftmaker, ap: 70, tags: "SpellVamp"),
            [DoransBlade] = Of(DoransBlade, ad: 10, tags: "LifeSteal"),
        };
    }

    private static ItemEvent Bought(int itemId, int atMs) => new() { EventType = ItemEventTypes.Purchased, ItemId = itemId, TimestampMs = atMs };

    private static ItemEvent Sold(int itemId, int atMs) => new() { EventType = ItemEventTypes.Sold, ItemId = itemId, TimestampMs = atMs };

    private static ItemEvent Destroyed(int itemId, int atMs) => new() { EventType = ItemEventTypes.Destroyed, ItemId = itemId, TimestampMs = atMs };

    private static ItemEvent Undo(int before, int after, int atMs)
        => new() { EventType = ItemEventTypes.Undo, ItemId = 0, BeforeId = before, AfterId = after, TimestampMs = atMs };
}
