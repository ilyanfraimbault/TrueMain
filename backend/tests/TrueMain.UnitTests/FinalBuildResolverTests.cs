using Data.Entities;
using FluentAssertions;
using Ingestor.Processes.Components.PatternAggregation;
using TrueMain.UnitTests.Fixtures;

namespace TrueMain.UnitTests;

public sealed class FinalBuildResolverTests
{
    private static IReadOnlyDictionary<int, ItemMetadata> Metadata => ItemMetadataFixtures.ItemMetadataById;

    [Fact]
    public void Resolve_keeps_only_final_completed_items_in_completion_order()
    {
        var buildItems = FinalBuildResolver.Resolve(
        [
            new ItemEvent { TimestampMs = 1000, ItemId = 1055, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 5000, ItemId = 3006, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12000, ItemId = 3153, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 18000, ItemId = 3031, EventType = "ITEM_PURCHASED" }
        ], [3153, 3006, 3031, 1055, 0, 0], [1055], Metadata);

        buildItems.Should().Equal(3153, 3031);
    }

    [Fact]
    public void Resolve_excludes_base_boots_and_intermediate_items()
    {
        var buildItems = FinalBuildResolver.Resolve(
        [
            new ItemEvent { TimestampMs = 1000, ItemId = 1001, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 5000, ItemId = 3006, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12000, ItemId = 3085, EventType = "ITEM_PURCHASED" }
        ], [3006, 3085, 1055, 0, 0, 0], [1055], Metadata);

        buildItems.Should().Equal(3085);
    }

    [Fact]
    public void Resolve_uses_the_latest_reacquisition_time_for_final_items()
    {
        var buildItems = FinalBuildResolver.Resolve(
        [
            new ItemEvent { TimestampMs = 5_000, ItemId = 3153, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 9_000, ItemId = 3153, EventType = "ITEM_SOLD" },
            new ItemEvent { TimestampMs = 12_000, ItemId = 3006, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 18_000, ItemId = 3153, EventType = "ITEM_PURCHASED" }
        ], [3153, 3006, 0, 0, 0, 0], [], Metadata);

        buildItems.Should().Equal(3153);
    }

    [Fact]
    public void Resolve_ignores_trinket_slot_items()
    {
        var buildItems = FinalBuildResolver.Resolve(
        [
            new ItemEvent { TimestampMs = 5_000, ItemId = 3153, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12_000, ItemId = 3006, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 18_000, ItemId = 3340, EventType = "ITEM_PURCHASED" }
        ], [3153, 3006, 3340, 0, 0, 0, 0], [], Metadata);

        buildItems.Should().Equal(3153);
    }

    [Fact]
    public void Resolve_ignores_cull_in_final_build()
    {
        var buildItems = FinalBuildResolver.Resolve(
        [
            new ItemEvent { TimestampMs = 5_000, ItemId = 1083, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12_000, ItemId = 3153, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 18_000, ItemId = 3031, EventType = "ITEM_PURCHASED" }
        ], [1083, 3153, 3031, 0, 0, 0, 0], [], Metadata);

        buildItems.Should().Equal(3153, 3031);
    }

    [Fact]
    public void Resolve_includes_inventory_transforms_using_their_source_purchase_time()
    {
        var buildItems = FinalBuildResolver.Resolve(
        [
            new ItemEvent { TimestampMs = 5_000, ItemId = 3004, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12_000, ItemId = 3153, EventType = "ITEM_PURCHASED" }
        ], [3042, 3153, 0, 0, 0, 0], [], Metadata);

        buildItems.Should().Equal(3004, 3153);
    }

    [Fact]
    public void Resolve_displays_the_source_item_using_the_transform_completion_time()
    {
        var buildItems = FinalBuildResolver.Resolve(
        [
            new ItemEvent { TimestampMs = 5_000, ItemId = 3004, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12_000, ItemId = 3153, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 20_000, ItemId = 3042, EventType = "ITEM_PURCHASED" }
        ], [3042, 3153, 0, 0, 0, 0], [], Metadata);

        buildItems.Should().Equal(3153, 3004);
    }
}
