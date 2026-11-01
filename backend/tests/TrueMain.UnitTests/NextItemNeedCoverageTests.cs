using System.Collections.Frozen;
using AwesomeAssertions;
using Data.BuildFacts;
using Data.Entities;
using Data.ItemContext;
using Ingestor.Options;
using Ingestor.Processes.Components.ItemContextAggregation;
using TrueMain.Services.Champions.NextItem;

namespace TrueMain.UnitTests;

/// <summary>
/// The advice is a need, not an item (#2371): an off-advice purchase covers the situational
/// need it meets, and the next suggestion does not ask for that need again.
/// </summary>
public sealed class NextItemNeedCoverageTests
{
    private const int Champion = 266;
    private const string Position = "TOP";
    private const string Patch = "16.4";

    private const int Root = 0;
    private const int Spirit = 3065;      // Spirit Visage — magic resistance, the mains' MR answer here
    private const int Kaenic = 2504;      // Kaenic Rookern — magic resistance, rarely the mains' choice
    private const int Thornmail = 3075;   // armour + anti-heal
    private const int Sunfire = 3068;     // armour
    private const int Titanic = 3748;     // health and damage: no situational need
    private const int Mantle = 1033;      // Null-Magic Mantle — a component, never a covered need

    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly IReadOnlyDictionary<ItemContextAxis, ItemContextBucket> NoSituation =
        new Dictionary<ItemContextAxis, ItemContextBucket>();

    private static readonly IReadOnlyDictionary<int, ItemNeed> Needs = ItemNeeds.Catalog(new Dictionary<int, ItemMetadata>
    {
        [Spirit] = Legendary(Spirit, "SpellBlock", "Health"),
        [Kaenic] = Legendary(Kaenic, "SpellBlock", "Health"),
        [Thornmail] = Legendary(Thornmail, "Armor", "Health") with { GrantsGrievousWounds = true },
        [Sunfire] = Legendary(Sunfire, "Armor", "Health"),
        [Titanic] = Legendary(Titanic, "Damage", "Health"),
        [Mantle] = new ItemMetadata(Mantle, 400, true, false, false, false, false, false) { Categories = Set("SpellBlock") },
    });

    [Fact]
    public void AnItemsNeedsAreReadOffItsCategoriesAndItsGrievousWounds()
    {
        ItemNeeds.Of(Legendary(Thornmail, "Armor", "Health") with { GrantsGrievousWounds = true })
            .Should().Be(ItemNeed.Armor | ItemNeed.AntiHeal);
        ItemNeeds.Of(Legendary(3135, "SpellDamage", "MagicPenetration")).Should().Be(ItemNeed.MagicPenetration);
        ItemNeeds.Of(Legendary(3036, "Damage", "ArmorPenetration")).Should().Be(ItemNeed.ArmorPenetration);
        ItemNeeds.Of(Legendary(3111, "SpellBlock", "Tenacity")).Should().Be(ItemNeed.MagicResist | ItemNeed.Tenacity);
        ItemNeeds.Of(Legendary(Titanic, "Damage", "Health")).Should().Be(ItemNeed.None);
    }

    [Fact]
    public void TheCatalogueHoldsCompletedLegendariesOnly()
    {
        Needs.Should().ContainKey(Spirit);
        Needs.Should().NotContainKey(Mantle, "a component is a step towards an item, not a need covered");
    }

    [Fact]
    public void AnOffAdviceItemThatMeetsTheSameNeedClosesIt()
    {
        // After Sunfire the mains go Spirit Visage (MR) or Thornmail. This game took Kaenic
        // Rookern instead: magic resistance is covered, so Spirit Visage is not suggested again.
        var model = Model(
            [Stat(Root, Sunfire, 1000), Stat(Sunfire, Spirit, 700), Stat(Sunfire, Thornmail, 300)],
            [Total(Root, 1000), Total(Sunfire, 1000)]);
        var items = new[] { Sunfire, Kaenic };
        var owned = items.ToHashSet();

        var offAdvice = model.OffAdviceItems(items);
        var covered = NextItemNeedCoverage.Covered(offAdvice, Needs);
        var branch = model.Branch(ItemContextSlot.Build, Sunfire)!;
        var scores = NextItemScorer.Score(branch, NoSituation, NextItemNeedCoverage.Excluded(branch, owned, covered, Needs));

        offAdvice.Should().Equal(Kaenic);
        covered.Should().Be(ItemNeed.MagicResist);
        scores.Select(score => score.ItemId).Should().Equal(Thornmail);
        scores[0].Share.Should().BeApproximately(1, 1e-9);
    }

    [Fact]
    public void ItemsTheMainsWouldHaveTakenCoverNothing()
    {
        var model = Model(
            [Stat(Root, Sunfire, 1000), Stat(Sunfire, Spirit, 700), Stat(Sunfire, Thornmail, 300)],
            [Total(Root, 1000), Total(Sunfire, 1000)]);

        model.OffAdviceItems([Sunfire, Thornmail]).Should().BeEmpty();
        NextItemNeedCoverage.Covered([], Needs).Should().Be(ItemNeed.None);
    }

    [Fact]
    public void WithNothingCoveredOnlyTheItemsHeldAreExcluded()
    {
        var model = Model(
            [Stat(Root, Sunfire, 1000), Stat(Sunfire, Spirit, 700), Stat(Sunfire, Thornmail, 300)],
            [Total(Root, 1000), Total(Sunfire, 1000)]);
        var owned = new HashSet<int> { Sunfire };

        NextItemNeedCoverage.Excluded(model.Branch(ItemContextSlot.Build, Sunfire)!, owned, ItemNeed.None, Needs)
            .Should().BeSameAs(owned);
    }

    [Fact]
    public void AnOffAdviceItemThatABranchContainsIsFollowed()
    {
        // Thornmail is never a first item, but the mains do build after it: the game continues
        // from Thornmail's branch, and Thornmail itself covers armour and anti-heal.
        var model = Model(
            [Stat(Root, Sunfire, 1000), Stat(Sunfire, Thornmail, 1000), Stat(Thornmail, Spirit, 600), Stat(Thornmail, Titanic, 400)],
            [Total(Root, 1000), Total(Sunfire, 1000), Total(Thornmail, 1000)]);

        var (path, _) = model.ResolveBuildPath([Thornmail]);

        path[^1].Should().Be(Thornmail);
        NextItemNeedCoverage.Covered(model.OffAdviceItems([Thornmail]), Needs).Should().Be(ItemNeed.Armor | ItemNeed.AntiHeal);
    }

    private static ItemMetadata Legendary(int id, params string[] categories)
        => new(id, 3000, true, false, false, false, true, false) { Categories = Set(categories) };

    private static FrozenSet<string> Set(params string[] categories) => categories.ToFrozenSet(StringComparer.Ordinal);

    private static NextItemModel Model(ChampionItemContextStat[] stats, ChampionItemContextTotal[] totals)
        => NextItemModel.From(NextItemTermBuilder.Build(
            new ItemContextScope(Champion, Position, Patch), stats, totals, [Patch], new NextItemModelOptions(), Now));

    private static ChampionItemContextStat Stat(int parent, int item, int games)
        => new()
        {
            ChampionId = Champion,
            Position = Position,
            Patch = Patch,
            Slot = ItemContextSlot.Build,
            ParentItemId = parent,
            ItemId = item,
            Axis = ItemContextAxis.Overall,
            Bucket = ItemContextBucket.All,
            Games = games,
            Wins = games / 2,
        };

    private static ChampionItemContextTotal Total(int parent, int games)
        => new()
        {
            ChampionId = Champion,
            Position = Position,
            Patch = Patch,
            Slot = ItemContextSlot.Build,
            ParentItemId = parent,
            Axis = ItemContextAxis.Overall,
            Bucket = ItemContextBucket.All,
            Games = games,
            Wins = games / 2,
        };
}
