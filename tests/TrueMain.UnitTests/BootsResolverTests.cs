using Data.Entities;
using FluentAssertions;
using Ingestor.Processes.Components.PatternAggregation;
using TrueMain.UnitTests.Fixtures;

namespace TrueMain.UnitTests;

public sealed class BootsResolverTests
{
    private static IReadOnlyDictionary<int, ItemMetadata> Metadata => ItemMetadataFixtures.ItemMetadataById;

    [Fact]
    public void Resolve_returns_the_most_relevant_boots_from_final_inventory()
    {
        var bootsItemId = BootsResolver.Resolve(
            [new ItemEvent { TimestampMs = 5_000, ItemId = 3006, EventType = "ITEM_PURCHASED" }],
            [3153, 1001, 3006, 3031, 0, 0, 0], [], Metadata);

        bootsItemId.Should().Be(3006);
    }

    [Fact]
    public void Resolve_uses_purchased_boots_even_when_they_are_missing_from_final_inventory()
    {
        var bootsItemId = BootsResolver.Resolve(
        [
            new ItemEvent { TimestampMs = 120_000, ItemId = 3006, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 720_000, ItemId = 3006, EventType = "ITEM_DESTROYED" }
        ], [3153, 3124, 3302, 3082, 0, 0, 0], [], Metadata);

        bootsItemId.Should().Be(3006);
    }

    [Fact]
    public void Resolve_ignores_undone_boot_purchases()
    {
        var bootsItemId = BootsResolver.Resolve(
        [
            new ItemEvent { TimestampMs = 120_000, ItemId = 3006, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 121_000, ItemId = 3006, BeforeId = 3006, EventType = "ITEM_UNDO" }
        ], [3153, 3124, 3302, 3082, 0, 0, 0], [], Metadata);

        bootsItemId.Should().Be(0);
    }
}
