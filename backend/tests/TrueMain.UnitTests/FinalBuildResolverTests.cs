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
        // Cull (1083) is flagged via IsStarterClassItem in the fixture rather
        // than the legacy hardcoded IgnoredFinalBuildItemIds — same outcome,
        // routed through the dynamic detector that also catches Doran's and
        // jungle pets.
        var buildItems = FinalBuildResolver.Resolve(
        [
            new ItemEvent { TimestampMs = 5_000, ItemId = 1083, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12_000, ItemId = 3153, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 18_000, ItemId = 3031, EventType = "ITEM_PURCHASED" }
        ], [1083, 3153, 3031, 0, 0, 0, 0], [], Metadata);

        buildItems.Should().Equal(3153, 3031);
    }

    [Fact]
    public void Resolve_excludes_doran_item_bought_after_starter_window()
    {
        // Reproduces match NA1_5560060671 (Shaco TOP 16.10): the player
        // bought a non-Doran starter (Sapphire Crystal + Refillable Potion)
        // in the first 10 seconds, then bought Doran's Ring (1056) on a back
        // at 178s — well outside StarterItemAnalyzer's 120s window — and kept
        // it until end of game. The starter list passed to Resolve does not
        // contain 1056 (because the analyzer never saw it as a starter), so
        // the only thing that can keep it out of the build path is the
        // IsStarterClassItem flag. Asserts the fix: 1056 never appears in
        // BuildItem0..6 even when the late purchase landed in the final
        // inventory and the starter filter doesn't catch it.
        var buildItems = FinalBuildResolver.Resolve(
        [
            new ItemEvent { TimestampMs = 177_905, ItemId = 1056, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 864_048, ItemId = 3153, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 1_330_902, ItemId = 3031, EventType = "ITEM_PURCHASED" }
        ], [3153, 3031, 1056, 0, 0, 0, 0], [], Metadata);

        buildItems.Should().NotContain(1056);
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
    public void Resolve_excludes_support_quest_completion_from_the_build_path()
    {
        // Bloodsong (3877) is a support-quest completion — it belongs to
        // the starter slot via StarterItemAnalyzer, never as a build node.
        // Even when it shows up in the player's final inventory, the
        // resolver should drop it and surface only the genuine build items.
        var buildItems = FinalBuildResolver.Resolve(
        [
            new ItemEvent { TimestampMs = 5_000, ItemId = 3153, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12_000, ItemId = 3877, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 18_000, ItemId = 3031, EventType = "ITEM_PURCHASED" }
        ], [3153, 3877, 3031, 0, 0, 0, 0], [], Metadata);

        buildItems.Should().NotContain(3877);
        buildItems.Should().Equal(3153, 3031);
    }

    [Fact]
    public void Resolve_excludes_support_quest_root_from_the_build_path()
    {
        // 3899 is a synthetic support-quest root fixture: marked
        // IsSupportQuestStarter=true *and* IsFinalItem=true, and absent from
        // the starterItems filter — so the only thing that can exclude it
        // from the build path is the IsSupportQuestStarter check we're
        // validating here. (A realistic root like World Atlas would also be
        // filtered by IsFinalItem=false, masking what's actually under test.)
        var buildItems = FinalBuildResolver.Resolve(
        [
            new ItemEvent { TimestampMs = 5_000, ItemId = 3899, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12_000, ItemId = 3153, EventType = "ITEM_PURCHASED" }
        ], [3153, 3899, 0, 0, 0, 0, 0], [], Metadata);

        buildItems.Should().NotContain(3899);
        buildItems.Should().Equal(3153);
    }
}
