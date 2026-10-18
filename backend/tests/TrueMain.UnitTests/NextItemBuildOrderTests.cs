using AwesomeAssertions;
using Data.BuildFacts;
using Data.Entities;
using Data.ItemContext;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions.NextItem;

namespace TrueMain.UnitTests;

/// <summary>
/// The build order of #2101: when a game's boots came (<see cref="BootsStepResolver"/>), the
/// step the model reads off those counts, and the row the next-item answer lays out from it.
/// </summary>
public sealed class NextItemBuildOrderTests
{
    private const int First = 3078;
    private const int Second = 6333;
    private const int Third = 3053;
    private const int Plated = 3047;

    private static readonly IReadOnlyDictionary<ItemContextAxis, ItemContextBucket> NoSituation =
        new Dictionary<ItemContextAxis, ItemContextBucket>();

    [Fact]
    public void BootsBoughtBeforeAnyLegendaryAreTheFirstStep()
        => BootsStepResolver.Resolve([Bought(Plated, 300_000), Bought(First, 600_000)], [First], Plated).Should().Be(1);

    [Fact]
    public void BootsBoughtAfterTheFirstLegendaryAreTheSecondStep()
        => BootsStepResolver.Resolve([Bought(First, 500_000), Bought(Plated, 600_000), Bought(Second, 900_000)], [First, Second], Plated)
            .Should().Be(2);

    [Fact]
    public void BootsTheTimelineNeverShowsBeingBoughtHaveNoStep()
        => BootsStepResolver.Resolve([Bought(First, 500_000)], [First], Plated).Should().Be(0);

    [Fact]
    public void AnUndoneBootsPurchaseDoesNotCount()
        => BootsStepResolver.Resolve(
            [
                Bought(Plated, 100_000),
                new ItemEvent { TimestampMs = 101_000, EventType = "ITEM_UNDO", BeforeId = Plated },
                Bought(First, 500_000),
                Bought(Plated, 600_000),
            ],
            [First],
            Plated).Should().Be(2);

    [Fact]
    public void TheBootsStepIsTheMostCommonOne_OnceEnoughGamesSayIt()
    {
        var model = Model(bootsSteps: [(1, 30), (2, 70)]);

        model.BootsStep(minGames: 100).Should().Be(2);
        model.BootsStep(minGames: 101).Should().BeNull();
    }

    [Fact]
    public void AFreshGameReadsTheWholeBuild_BootsAfterTheFirstItem()
    {
        var order = Order(Model(bootsSteps: [(2, 80), (1, 20)]), items: []);

        order.Select(step => (step.ItemId, step.State)).Should().Equal(
            (First, NextItemOrderState.Next),
            (Plated, NextItemOrderState.Upcoming),
            (Second, NextItemOrderState.Upcoming),
            (Third, NextItemOrderState.Upcoming));
        order[1].IsBoots.Should().BeTrue();
    }

    [Fact]
    public void BootsDueNowAreTheNextStep()
    {
        var order = Order(Model(bootsSteps: [(2, 100)]), items: [First]);

        order.Select(step => (step.ItemId, step.State)).Should().Equal(
            (First, NextItemOrderState.Completed),
            (Plated, NextItemOrderState.Next),
            (Second, NextItemOrderState.Upcoming),
            (Third, NextItemOrderState.Upcoming));
    }

    [Fact]
    public void LateBootsMoveUpToTheNextStep()
    {
        var order = Order(Model(bootsSteps: [(1, 100)]), items: [First, Second]);

        order.Select(step => step.ItemId).Should().Equal(First, Second, Plated, Third);
        order[2].State.Should().Be(NextItemOrderState.Next);
    }

    [Fact]
    public void HeldBootsStayWhereTheyWereBought()
    {
        var model = Model(bootsSteps: [(1, 100)]);
        var order = NextItemBuildOrder.Resolve(model, [First, Plated], NoSituation, Slot(Second), boots: null);

        order.Select(step => (step.ItemId, step.State)).Should().Equal(
            (First, NextItemOrderState.Completed),
            (Plated, NextItemOrderState.Completed),
            (Second, NextItemOrderState.Next),
            (Third, NextItemOrderState.Upcoming));
    }

    [Fact]
    public void WithoutATimingTheRowLeavesTheBootsOut()
    {
        var order = Order(Model(bootsSteps: []), items: []);

        order.Should().NotContain(step => step.IsBoots);
        order.Select(step => step.ItemId).Should().Equal(First, Second, Third);
    }

    private static IReadOnlyList<NextItemOrderStepReadModel> Order(NextItemModel model, IReadOnlyList<int> items)
    {
        var next = items.Count == 0 ? First : items.Count == 1 ? Second : Third;
        return NextItemBuildOrder.Resolve(model, items, NoSituation, Slot(next), Slot(Plated));
    }

    private static NextItemSlotReadModel Slot(int itemId)
        => new() { Candidates = [new NextItemCandidateReadModel { ItemId = itemId, Share = 1, BaseShare = 1 }] };

    /// <summary>A three-item chain First → Second → Third, one pair of boots, and the given boots timings.</summary>
    private static NextItemModel Model(IReadOnlyList<(int Step, int Games)> bootsSteps)
    {
        var terms = new List<ChampionNextItemTerm>
        {
            Term(ItemContextSlot.Build, 0, First, 100, 100),
            Term(ItemContextSlot.Build, First, Second, 100, 100),
            Term(ItemContextSlot.Build, Second, Third, 100, 100),
            Term(ItemContextSlot.Boots, 0, Plated, 100, 100),
        };

        var timingGames = bootsSteps.Sum(step => step.Games);
        terms.AddRange(bootsSteps.Select(step => Term(ItemContextSlot.BootsTiming, 0, step.Step, step.Games, timingGames)));

        return NextItemModel.From(terms);
    }

    private static ChampionNextItemTerm Term(ItemContextSlot slot, int parent, int itemId, int games, int branchGames)
        => new()
        {
            ChampionId = 1,
            Position = "MIDDLE",
            Patch = "16.20",
            Slot = slot,
            ParentItemId = parent,
            ItemId = itemId,
            Axis = ItemContextAxis.Overall,
            Bucket = ItemContextBucket.All,
            Weight = Math.Log(games / (double)branchGames),
            Games = games,
            BranchGames = branchGames,
        };

    private static ItemEvent Bought(int itemId, int at)
        => new() { TimestampMs = at, EventType = "ITEM_PURCHASED", ItemId = itemId };
}
