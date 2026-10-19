using AwesomeAssertions;
using Data.BuildFacts;
using Data.Entities;
using Data.ItemContext;
using Ingestor.Options;
using Ingestor.Processes.Components.ItemContextAggregation;
using TrueMain.UnitTests.Fixtures;

namespace TrueMain.UnitTests;

/// <summary>
/// The fold side of #2101: a game with timed boots emits a <see cref="ItemContextSlot.BootsTiming"/>
/// edge, the next-item model serves it as terms, and no verdict card is ever written for it.
/// </summary>
public sealed class BootsTimingFoldTests
{
    private const int Champion = 103;
    private const string Position = "MIDDLE";
    private const string Patch = "16.20";
    private const int FirstLegendary = 3031;
    private const int SecondLegendary = 3153;
    private const int Boots = 3006;

    private static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ItemContextScope Scope = new(Champion, Position, Patch);

    private static readonly IReadOnlyDictionary<int, ItemMetadata> Metadata =
        new Dictionary<int, ItemMetadata>(ItemMetadataFixtures.ItemMetadataById)
        {
            [Boots] = new(Boots, 1100, InStore: true, IsConsumable: false, IsBootsItem: true, IsBaseBoots: false, IsFinalItem: true, IsFinalBoots: true),
        };

    [Fact]
    public void AGameWithTimedBootsEmitsTheStepTheyTook()
    {
        var edges = ItemContextSlotResolver.Resolve(
            [Bought(FirstLegendary, 500_000), Bought(Boots, 600_000), Bought(SecondLegendary, 900_000)],
            [FirstLegendary, Boots, SecondLegendary, 0, 0, 0, 0],
            Metadata);

        var timing = edges[ItemContextSlot.BootsTiming].Should().ContainSingle().Subject;
        timing.ParentItemId.Should().Be(0);
        timing.ItemId.Should().Be(2, "the boots came right after the first legendary");
        timing.EligibleAxes.Should().BeEmpty("when the boots come is asked whatever the draft");
    }

    [Fact]
    public void BootsSeenOnlyInTheFinalInventoryEmitNoTiming()
    {
        var edges = ItemContextSlotResolver.Resolve(
            [Bought(FirstLegendary, 500_000)],
            [FirstLegendary, Boots, 0, 0, 0, 0, 0],
            Metadata);

        edges.Should().NotContainKey(ItemContextSlot.BootsTiming);
    }

    [Fact]
    public void TheModelServesTheTimingAsTerms()
    {
        var terms = NextItemTermBuilder.Build(
            Scope, [Stat(1, 30), Stat(2, 70)], [Total(100)], [Patch], new NextItemModelOptions(), Now);

        var step = terms.Single(term => term.Slot == ItemContextSlot.BootsTiming && term.ItemId == 2);
        step.Weight.Should().BeApproximately(Math.Log(0.7), 1e-9);
        NextItemModel.From(terms).BootsStep(minGames: 100).Should().Be(2);
    }

    [Fact]
    public void NoVerdictIsEverWrittenForTheTiming()
    {
        var verdicts = ItemContextVerdictBuilder.Build(
            Scope, [Stat(1, 300), Stat(2, 700)], [Total(1000)], [Patch], new ItemContextAggregationOptions(), Now);

        verdicts.Should().BeEmpty("a step number is not an item a card could name");
    }

    private static ItemEvent Bought(int itemId, int at)
        => new() { TimestampMs = at, EventType = "ITEM_PURCHASED", ItemId = itemId };

    private static ChampionItemContextStat Stat(int step, int games)
        => new()
        {
            ChampionId = Champion,
            Position = Position,
            Patch = Patch,
            Slot = ItemContextSlot.BootsTiming,
            ParentItemId = 0,
            ItemId = step,
            Axis = ItemContextAxis.Overall,
            Bucket = ItemContextBucket.All,
            Games = games,
            Wins = games / 2,
        };

    private static ChampionItemContextTotal Total(int games)
        => new()
        {
            ChampionId = Champion,
            Position = Position,
            Patch = Patch,
            Slot = ItemContextSlot.BootsTiming,
            ParentItemId = 0,
            Axis = ItemContextAxis.Overall,
            Bucket = ItemContextBucket.All,
            Games = games,
            Wins = games / 2,
        };
}
