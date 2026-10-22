using AwesomeAssertions;
using Data.BuildFacts;
using Data.Entities;
using TrueMain.Services.Champions.ItemRoutes;

namespace TrueMain.UnitTests;

/// <summary>
/// The build path of #2183: the order a game bought an item's components in
/// (<see cref="ComponentOrderResolver"/>), and the path the read lays out from those counts
/// (<see cref="ItemRouteBook"/>).
/// </summary>
public sealed class ComponentOrderResolverTests
{
    private const int Archangels = 3003;
    private const int Tear = 3070;
    private const int LostChapter = 3802;
    private const int Codex = 3108;
    private const int Tome = 1052;
    private const int Sapphire = 1027;
    private const int Mote = 2022;
    private const int Rabadons = 3089;
    private const int Rod = 1058;

    private static readonly IReadOnlyDictionary<int, ItemMetadata> Items = new Dictionary<int, ItemMetadata>
    {
        [Archangels] = Item(Archangels, 2900, Tear, LostChapter, Codex),
        [Tear] = Item(Tear, 400),
        [LostChapter] = Item(LostChapter, 1200, Tome, Sapphire, Mote),
        [Codex] = Item(Codex, 850, Tome, Mote),
        [Tome] = Item(Tome, 400),
        [Sapphire] = Item(Sapphire, 300),
        [Mote] = Item(Mote, 250),
        [Rabadons] = Item(Rabadons, 3500, Rod, Rod),
        [Rod] = Item(Rod, 1200),
    };

    [Fact]
    public void TheOrderIsTheFirstPurchase_OfEachComponent_NotTheRecipes()
    {
        IReadOnlyList<ItemEvent> timeline =
        [
            // Sapphire towards Lost Chapter first, then the Tear, then a Tome for Lost Chapter.
            Bought(Sapphire, 100),
            Bought(Tear, 200),
            Bought(Tome, 300),
            // Lost Chapter combines: the timeline destroys its parts at the purchase's own time.
            Destroyed(Sapphire, 400),
            Destroyed(Tome, 400),
            Bought(LostChapter, 400),
            // A Mote bought and taken back must not count.
            Bought(Mote, 450),
            Undo(Mote, 455),
            // The Codex is never completed on its own: its parts go straight into the staff.
            Bought(Tome, 500),
            Bought(Mote, 510),
            Destroyed(Tear, 600),
            Destroyed(LostChapter, 600),
            Destroyed(Tome, 600),
            Destroyed(Mote, 600),
            Bought(Archangels, 600),
        ];

        var orders = ComponentOrderResolver.Resolve(timeline, [Archangels], Items).ToDictionary(order => order.ItemId);

        // Lost Chapter ranks from the Sapphire bought towards it, ahead of the Tear.
        orders[Archangels].Ranks.Should().BeEquivalentTo(
            [new ComponentRank(Tear, 0, 2), new ComponentRank(LostChapter, 0, 1), new ComponentRank(Codex, 0, 3)]);

        // Its own order: the Mote was paid for with the combine, so it ranks after the others.
        orders[LostChapter].Ranks.Should().BeEquivalentTo(
            [new ComponentRank(Tome, 0, 2), new ComponentRank(Sapphire, 0, 1), new ComponentRank(Mote, 0, 3)]);

        // The undone Mote at 450 was never held: the Codex's Mote is the one of 510.
        orders[Codex].Ranks.Should().BeEquivalentTo([new ComponentRank(Tome, 0, 1), new ComponentRank(Mote, 0, 2)]);
    }

    [Fact]
    public void AComponentAlreadySpentOnAnotherItem_DoesNotCountForTheNextOne()
    {
        IReadOnlyList<ItemEvent> timeline =
        [
            Bought(Tome, 100),
            Bought(Mote, 150),
            Destroyed(Tome, 200),
            Destroyed(Mote, 200),
            Bought(Codex, 200),
            // The Codex is sold: the Tome that went into it is gone with it.
            Sold(Codex, 300),
            Bought(Tear, 400),
            Bought(Sapphire, 500),
            Destroyed(Tear, 900),
            Destroyed(Sapphire, 900),
            Bought(Archangels, 900),
        ];

        var orders = ComponentOrderResolver.Resolve(timeline, [Archangels], Items).ToDictionary(order => order.ItemId);

        orders[Archangels].Ranks.Should().BeEquivalentTo(
            [new ComponentRank(Tear, 0, 1), new ComponentRank(LostChapter, 0, 2), new ComponentRank(Codex, 0, 3)]);
    }

    [Fact]
    public void TwoCopiesOfAComponentAreTwoSlots()
    {
        IReadOnlyList<ItemEvent> timeline =
        [
            Bought(Rod, 100),
            Bought(Rod, 300),
            Destroyed(Rod, 400),
            Destroyed(Rod, 400),
            Bought(Rabadons, 400),
        ];

        ComponentOrderResolver.Resolve(timeline, [Rabadons], Items).Should().ContainSingle()
            .Which.Ranks.Should().BeEquivalentTo([new ComponentRank(Rod, 0, 1), new ComponentRank(Rod, 1, 2)]);
    }

    [Fact]
    public void AnItemBoughtOutrightSaysNothingAboutTheOrder()
        => ComponentOrderResolver.Resolve([Bought(Rabadons, 400)], [Rabadons], Items).Should().BeEmpty();

    [Fact]
    public void AnUndoneCompletionIsNotTheOneCounted()
    {
        IReadOnlyList<ItemEvent> timeline =
        [
            Bought(Rod, 100),
            Bought(Rabadons, 200),
            // Taken back: the Rod returns to the inventory.
            Undo(Rabadons, 205),
            Bought(Rod, 300),
            Bought(Rabadons, 400),
        ];

        ComponentOrderResolver.Resolve(timeline, [Rabadons], Items).Should().ContainSingle()
            .Which.Ranks.Should().BeEquivalentTo([new ComponentRank(Rod, 0, 1), new ComponentRank(Rod, 1, 2)]);
    }

    [Fact]
    public void TheBookOrdersByMeanRank_AndCarriesTheShareAtThatRank()
    {
        // 30 games: Lost Chapter first in 20, the Tear first in 10; the Codex always last.
        var book = Book(
            [
                Count(Archangels, LostChapter, 1, 20), Count(Archangels, LostChapter, 2, 10),
                Count(Archangels, Tear, 1, 10), Count(Archangels, Tear, 2, 20),
                Count(Archangels, Codex, 3, 30),
            ]);

        var route = book.Route(Archangels);

        route.Select(step => step.ItemId).Should().Equal(LostChapter, Tear, Codex);
        route.Select(step => step.Rank).Should().Equal(1, 2, 3);
        route[0].Share.Should().BeApproximately(20 / 30d, 1e-9);
        route[0].Cost.Should().Be(1200);
        route.Should().OnlyContain(step => step.Measured);
        book.Games(Archangels).Should().Be(30);

        // Lost Chapter's own components have no counts: they come in recipe order, and say so.
        route[0].Components.Select(step => step.ItemId).Should().Equal(Tome, Sapphire, Mote);
        route[0].Components.Should().OnlyContain(step => !step.Measured && step.Share == null);
    }

    [Fact]
    public void UnderTheFloor_ThePathIsTheRecipes()
    {
        var book = Book([Count(Archangels, Codex, 1, 5), Count(Archangels, Tear, 2, 5), Count(Archangels, LostChapter, 3, 5)]);

        var route = book.Route(Archangels);

        route.Select(step => step.ItemId).Should().Equal(Tear, LostChapter, Codex);
        route.Should().OnlyContain(step => !step.Measured);
    }

    [Fact]
    public void AThinPatch_ReachesBackToTheOneBefore()
    {
        IReadOnlyList<ComponentOrderCount> served = [Count(Rabadons, Rod, 1, 5, occurrence: 1), Count(Rabadons, Rod, 2, 5)];
        IReadOnlyList<ComponentOrderCount> previous = [Count(Rabadons, Rod, 1, 20, occurrence: 1), Count(Rabadons, Rod, 2, 20)];

        var book = new ItemRouteBook([served, previous], Items);

        book.Games(Rabadons).Should().Be(25);
        book.Route(Rabadons).Should().OnlyContain(step => step.Measured);
    }

    private static ItemRouteBook Book(IReadOnlyList<ComponentOrderCount> counts) => new([counts], Items);

    private static ComponentOrderCount Count(int itemId, int componentId, int rank, int games, int occurrence = 0)
        => new(itemId, componentId, occurrence, rank, games);

    private static ItemMetadata Item(int id, int price, params int[] components)
        => new(id, price, true, false, false, false, components.Length > 0, false) { Components = components };

    private static ItemEvent Bought(int itemId, int at) => new() { ItemId = itemId, TimestampMs = at, EventType = "ITEM_PURCHASED" };

    private static ItemEvent Destroyed(int itemId, int at) => new() { ItemId = itemId, TimestampMs = at, EventType = "ITEM_DESTROYED" };

    private static ItemEvent Sold(int itemId, int at) => new() { ItemId = itemId, TimestampMs = at, EventType = "ITEM_SOLD" };

    private static ItemEvent Undo(int itemId, int at) => new() { TimestampMs = at, EventType = "ITEM_UNDO", BeforeId = itemId, AfterId = 0 };
}
