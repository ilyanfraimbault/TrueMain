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

    [Fact]
    public void ResolveOrdered_pins_support_quest_completion_to_slot_zero_for_UTILITY()
    {
        // Bloodsong (3877) completes mid-game so `Resolve` orders it after
        // the items purchased before the quest tipped over.
        var buildItems = FinalBuildResolver.ResolveOrdered(
        [
            new ItemEvent { TimestampMs = 5_000, ItemId = 3031, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12_000, ItemId = 3877, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 18_000, ItemId = 3085, EventType = "ITEM_PURCHASED" }
        ], [3031, 3877, 3085, 0, 0, 0, 0], [], "UTILITY", Metadata);

        buildItems.Should().Equal(3877, 3031, 3085);
    }

    [Fact]
    public void ResolveOrdered_leaves_build_alone_for_non_UTILITY_positions()
    {
        // Same events as the UTILITY hoist test but on BOTTOM (e.g. Senna ADC
        // running Relic Shield) — the completion should stay wherever its
        // timestamp puts it.
        var buildItems = FinalBuildResolver.ResolveOrdered(
        [
            new ItemEvent { TimestampMs = 5_000, ItemId = 3031, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12_000, ItemId = 3877, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 18_000, ItemId = 3085, EventType = "ITEM_PURCHASED" }
        ], [3031, 3877, 3085, 0, 0, 0, 0], [], "BOTTOM", Metadata);

        buildItems.Should().Equal(3031, 3877, 3085);
    }

    [Fact]
    public void ResolveOrdered_is_a_noop_for_UTILITY_without_any_completion_item()
    {
        var buildItems = FinalBuildResolver.ResolveOrdered(
        [
            new ItemEvent { TimestampMs = 5_000, ItemId = 3031, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12_000, ItemId = 3085, EventType = "ITEM_PURCHASED" }
        ], [3031, 3085, 0, 0, 0, 0, 0], [], "UTILITY", Metadata);

        buildItems.Should().Equal(3031, 3085);
    }

    [Fact]
    public void ResolveOrdered_is_a_noop_when_completion_already_at_slot_zero()
    {
        var buildItems = FinalBuildResolver.ResolveOrdered(
        [
            new ItemEvent { TimestampMs = 5_000, ItemId = 3877, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12_000, ItemId = 3031, EventType = "ITEM_PURCHASED" }
        ], [3877, 3031, 0, 0, 0, 0, 0], [], "UTILITY", Metadata);

        buildItems.Should().Equal(3877, 3031);
    }

    [Fact]
    public void ResolveOrdered_uses_case_insensitive_position_matching()
    {
        var buildItems = FinalBuildResolver.ResolveOrdered(
        [
            new ItemEvent { TimestampMs = 5_000, ItemId = 3031, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12_000, ItemId = 3877, EventType = "ITEM_PURCHASED" }
        ], [3031, 3877, 0, 0, 0, 0, 0], [], "utility", Metadata);

        buildItems.Should().Equal(3877, 3031);
    }

    [Fact]
    public void ResolveOrdered_treats_null_position_as_non_UTILITY()
    {
        var buildItems = FinalBuildResolver.ResolveOrdered(
        [
            new ItemEvent { TimestampMs = 5_000, ItemId = 3031, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12_000, ItemId = 3877, EventType = "ITEM_PURCHASED" }
        ], [3031, 3877, 0, 0, 0, 0, 0], [], position: null, Metadata);

        buildItems.Should().Equal(3031, 3877);
    }

    [Fact]
    public void ResolveOrdered_hoists_only_the_first_completion_when_two_are_present()
    {
        // Pathological inventory with both Bloodsong and Solstice Sleigh
        // (couldn't happen in a normal game). The earliest-completed one
        // wins slot 0; the second one stays at its post-Resolve position.
        var buildItems = FinalBuildResolver.ResolveOrdered(
        [
            new ItemEvent { TimestampMs = 5_000, ItemId = 3031, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 9_000, ItemId = 3877, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 18_000, ItemId = 3876, EventType = "ITEM_PURCHASED" }
        ], [3031, 3877, 3876, 0, 0, 0, 0], [], "UTILITY", Metadata);

        buildItems.Should().Equal(3877, 3031, 3876);
    }
}
