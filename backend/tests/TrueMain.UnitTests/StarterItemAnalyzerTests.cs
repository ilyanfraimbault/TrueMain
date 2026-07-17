using Data.BuildFacts;
using Data.Entities;
using AwesomeAssertions;
using TrueMain.UnitTests.Fixtures;

namespace TrueMain.UnitTests;

public sealed class StarterItemAnalyzerTests
{
    private static IReadOnlyDictionary<int, ItemMetadata> Metadata => ItemMetadataFixtures.ItemMetadataById;

    [Fact]
    public void BuildStarterItems_keeps_early_purchases_within_500_gold()
    {
        var starterItems = StarterItemAnalyzer.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 1000, ItemId = 1055, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 1500, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 95_000, ItemId = 1001, EventType = "ITEM_PURCHASED" }
        ], Metadata);

        starterItems.Should().Equal(1055, 2003);
    }

    [Fact]
    public void BuildStarterItems_keeps_the_valid_subset_when_later_early_purchases_would_exceed_500_gold()
    {
        var starterItems = StarterItemAnalyzer.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 1000, ItemId = 1001, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 1500, ItemId = 1055, EventType = "ITEM_PURCHASED" }
        ], Metadata);

        starterItems.Should().Equal(1001);
    }

    [Fact]
    public void BuildStarterItems_respects_undo_and_keeps_the_final_starter_basket()
    {
        var starterItems = StarterItemAnalyzer.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 1_000, ItemId = 1001, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 1_500, ItemId = 1001, BeforeId = 1001, EventType = "ITEM_UNDO" },
            new ItemEvent { TimestampMs = 2_000, ItemId = 1055, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 2_500, ItemId = 2003, EventType = "ITEM_PURCHASED" }
        ], Metadata);

        starterItems.Should().Equal(1055, 2003);
    }

    [Fact]
    public void BuildStarterItems_ignores_starter_trinkets_instead_of_dropping_the_whole_basket()
    {
        var starterItems = StarterItemAnalyzer.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 1_000, ItemId = 3340, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 2_000, ItemId = 1055, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 3_000, ItemId = 2003, EventType = "ITEM_PURCHASED" }
        ], Metadata);

        starterItems.Should().Equal(1055, 2003);
    }

    [Fact]
    public void BuildStarterItems_keeps_purchased_potions_even_if_they_are_consumed_before_two_minutes()
    {
        var starterItems = StarterItemAnalyzer.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 10_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 11_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 45_000, ItemId = 2003, EventType = "ITEM_DESTROYED" },
            new ItemEvent { TimestampMs = 66_000, ItemId = 2003, EventType = "ITEM_DESTROYED" }
        ], Metadata);

        starterItems.Should().Equal(2003, 2003);
    }

    [Fact]
    public void BuildStarterItems_ignores_purchases_that_would_push_the_starter_above_500_gold()
    {
        var starterItems = StarterItemAnalyzer.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 1_000, ItemId = 1001, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 1_500, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 2_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 2_500, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 3_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 3_500, ItemId = 2003, EventType = "ITEM_PURCHASED" }
        ], Metadata);

        starterItems.Should().Equal(1001, 2003, 2003, 2003, 2003);
    }

    [Fact]
    public void BuildStarterItems_keeps_the_current_valid_basket_when_a_later_purchase_would_overflow()
    {
        var starterItems = StarterItemAnalyzer.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 1_000, ItemId = 3070, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 1_500, ItemId = 1056, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 2_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 2_500, ItemId = 2003, EventType = "ITEM_PURCHASED" }
        ], Metadata);

        starterItems.Should().Equal(3070, 2003, 2003);
    }

    [Fact]
    public void BuildStarterItems_ignores_a_second_shop_batch_after_a_large_gap()
    {
        var starterItems = StarterItemAnalyzer.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 1_000, ItemId = 1055, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 2_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 50_000, ItemId = 3070, EventType = "ITEM_PURCHASED" }
        ], Metadata);

        starterItems.Should().Equal(1055, 2003);
    }

    [Fact]
    public void BuildStarterItems_infers_support_starter_when_quest_chain_exists_without_initial_purchase_event()
    {
        var starterItems = StarterItemAnalyzer.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 3_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 3_200, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 3_400, ItemId = 3340, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 420_000, ItemId = 3865, EventType = "ITEM_DESTROYED" },
            new ItemEvent { TimestampMs = 794_000, ItemId = 3866, EventType = "ITEM_DESTROYED" },
            new ItemEvent { TimestampMs = 807_000, ItemId = 3867, EventType = "ITEM_DESTROYED" }
        ], Metadata);

        starterItems.Should().Equal(3865, 2003, 2003);
    }

    [Fact]
    public void Analyze_does_not_count_inferred_support_starter_toward_paid_starter_cost()
    {
        var analysis = StarterItemAnalyzer.Analyze(
        [
            new ItemEvent { TimestampMs = 3_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 3_200, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 420_000, ItemId = 3865, EventType = "ITEM_DESTROYED" }
        ], Metadata);

        analysis.Items.Should().Equal(3865, 2003, 2003);
        analysis.TotalCost.Should().Be(100);
    }

    [Fact]
    public void BuildStarterItems_prefers_completion_over_root_when_quest_finished_during_match()
    {
        // Player completed the support quest mid-match: events show the
        // chain (root destroyed, intermediates destroyed) plus the final
        // completion appearing in store. The starter slot should reflect
        // what the player actually owned at the end — the completion.
        var starterItems = StarterItemAnalyzer.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 3_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 3_200, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 3_400, ItemId = 3340, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 420_000, ItemId = 3865, EventType = "ITEM_DESTROYED" },
            new ItemEvent { TimestampMs = 794_000, ItemId = 3866, EventType = "ITEM_DESTROYED" },
            new ItemEvent { TimestampMs = 807_000, ItemId = 3867, EventType = "ITEM_DESTROYED" },
            new ItemEvent { TimestampMs = 808_000, ItemId = 3877, EventType = "ITEM_PURCHASED" }
        ], Metadata);

        starterItems.Should().Contain(3877);
        starterItems.Should().NotContain(3865);
    }

    [Fact]
    public void BuildStarterItems_falls_back_to_root_when_quest_chain_appears_without_completion()
    {
        // Player surrendered early or the quest didn't finish: only the
        // root / intermediates show up in the events. We surface the root
        // so the player's lane intent ("they were on support") is preserved.
        var starterItems = StarterItemAnalyzer.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 3_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 3_200, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 420_000, ItemId = 3866, EventType = "ITEM_DESTROYED" }
        ], Metadata);

        starterItems.Should().Equal(3865, 2003, 2003);
    }

    [Fact]
    public void BuildStarterItems_keeps_completion_already_present_without_inferring_root()
    {
        // Completion observed directly in events as a purchase. Don't
        // double-add the root on top — the player already has the right
        // family member in their basket.
        var starterItems = StarterItemAnalyzer.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 3_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 60_000, ItemId = 3877, EventType = "ITEM_PURCHASED" }
        ], Metadata);

        starterItems.Should().Contain(3877);
        starterItems.Should().NotContain(3865);
    }

    [Fact]
    public void BuildStarterItems_detects_completion_via_final_inventory_when_event_missing()
    {
        // Real Riot timelines often omit the ITEM_PURCHASED for the support
        // quest completion choice — only the intermediates' ITEM_DESTROYED
        // events are reliable. Cross-checking the player's end-of-game
        // inventory (finalItems) lets us surface the completion anyway.
        // Mirrors the actual 16.10 Nautilus support shape verified against
        // prod data: World Atlas is auto-gifted (no early purchase event),
        // only DESTROYED events for 3865/3866/3867 show up in the timeline,
        // but Bloodsong (3877) sits in finalItems.
        var starterItems = StarterItemAnalyzer.BuildStarterItems(
            [
                new ItemEvent { TimestampMs = 1_500, ItemId = 3340, EventType = "ITEM_PURCHASED" },
                new ItemEvent { TimestampMs = 2_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
                new ItemEvent { TimestampMs = 2_500, ItemId = 2003, EventType = "ITEM_PURCHASED" },
                new ItemEvent { TimestampMs = 410_000, ItemId = 3865, EventType = "ITEM_DESTROYED" },
                new ItemEvent { TimestampMs = 762_000, ItemId = 3866, EventType = "ITEM_DESTROYED" },
                new ItemEvent { TimestampMs = 765_000, ItemId = 3867, EventType = "ITEM_DESTROYED" }
            ],
            finalItems: [3877, 3153, 3047, 0, 0, 0, 0],
            Metadata);

        starterItems.Should().Contain(3877);
        starterItems.Should().NotContain(3865);
    }

    [Fact]
    public void BuildStarterItems_falls_back_to_root_when_final_inventory_still_holds_intermediate()
    {
        // Game ended mid-quest: final inventory still shows the
        // intermediate (Bounty of Worlds, 3867) rather than a completion.
        // We surface the root so the lane intent is preserved without
        // misleadingly promoting an intermediate to the starter slot.
        var starterItems = StarterItemAnalyzer.BuildStarterItems(
            [
                new ItemEvent { TimestampMs = 2_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
                new ItemEvent { TimestampMs = 2_500, ItemId = 2003, EventType = "ITEM_PURCHASED" },
                new ItemEvent { TimestampMs = 410_000, ItemId = 3865, EventType = "ITEM_DESTROYED" },
                new ItemEvent { TimestampMs = 762_000, ItemId = 3866, EventType = "ITEM_DESTROYED" }
            ],
            finalItems: [3867, 3153, 0, 0, 0, 0, 0],
            Metadata);

        starterItems.Should().Contain(3865);
        starterItems.Should().NotContain(3867);
    }
}
