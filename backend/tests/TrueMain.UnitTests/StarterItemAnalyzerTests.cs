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

    [Fact]
    public void BuildStarterItems_ignores_a_support_completion_the_player_only_saw_destroyed()
    {
        // The production bug behind #923's report. A jungler's timeline carries six to
        // eight ITEM_DESTROYED events naming a support completion they never owned —
        // measured on preprod across Viego jungle games. Treating those as proof the
        // quest was finished injected Bloodsong (400 g) into the basket, past the 500 g
        // budget, and the page showed "Scorchclaw Pup + Bloodsong + Health Potion".
        var starterItems = StarterItemAnalyzer.BuildStarterItems(
            [
                new ItemEvent { TimestampMs = 1_000, ItemId = 1101, EventType = "ITEM_PURCHASED" },
                new ItemEvent { TimestampMs = 1_500, ItemId = 2003, EventType = "ITEM_PURCHASED" },
                new ItemEvent { TimestampMs = 620_000, ItemId = 3877, EventType = "ITEM_DESTROYED" },
                new ItemEvent { TimestampMs = 640_000, ItemId = 3877, EventType = "ITEM_DESTROYED" }
            ],
            finalItems: [3153, 3047, 0, 0, 0, 0, 0],
            Metadata);

        starterItems.Should().Equal(1101, 2003);
    }

    [Fact]
    public void Analyze_keeps_a_jungle_basket_within_budget_despite_destroyed_completions()
    {
        var analysis = StarterItemAnalyzer.Analyze(
            [
                new ItemEvent { TimestampMs = 1_000, ItemId = 1101, EventType = "ITEM_PURCHASED" },
                new ItemEvent { TimestampMs = 1_500, ItemId = 2003, EventType = "ITEM_PURCHASED" },
                new ItemEvent { TimestampMs = 620_000, ItemId = 3877, EventType = "ITEM_DESTROYED" }
            ],
            finalItems: [3153, 0, 0, 0, 0, 0, 0],
            Metadata);

        // 450 + 50: the budget is the readable invariant a reader checks the basket
        // against, and it was silently blown before this.
        analysis.TotalCost.Should().Be(500);
    }

    [Fact]
    public void BuildStarterItems_still_surfaces_a_completion_the_player_bought()
    {
        // The other side of the same rule: a real support buys the completion, so the
        // ownership signal is there and the starter must still show it.
        var starterItems = StarterItemAnalyzer.BuildStarterItems(
            [
                new ItemEvent { TimestampMs = 2_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
                new ItemEvent { TimestampMs = 410_000, ItemId = 3865, EventType = "ITEM_DESTROYED" },
                new ItemEvent { TimestampMs = 800_000, ItemId = 3877, EventType = "ITEM_PURCHASED" }
            ],
            Metadata);

        starterItems.Should().Contain(3877);
        starterItems.Should().NotContain(3865);
    }

    [Fact]
    public void BuildStarterItems_ignores_a_completion_named_only_as_an_undos_before_side()
    {
        // Flagged in review: EnumerateRelevantItemIds used to prove ownership of every
        // candidate an ITEM_UNDO touched — ItemId, BeforeId and AfterId alike. BeforeId is
        // what the player is giving back, not what they end up holding; trusting it would
        // reopen the exact bug this PR fixes for ITEM_DESTROYED, just through undo instead.
        // This isolates that: the completion appears solely as an undo's before-side (the
        // reverse transformation, intermediate -> completion undone back to intermediate),
        // never behind a real ITEM_PURCHASED of its own — so it must not surface.
        var starterItems = StarterItemAnalyzer.BuildStarterItems(
            [
                new ItemEvent { TimestampMs = 2_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
                // ItemId is 0 on a real ITEM_UNDO — only BeforeId/AfterId are populated
                // (see MatchParticipantEventJsonShapeTests) — matched here for fidelity.
                new ItemEvent { TimestampMs = 800_000, BeforeId = 3877, AfterId = 3866, EventType = "ITEM_UNDO" }
            ],
            Metadata);

        starterItems.Should().NotContain(3877);
    }

    [Fact]
    public void BuildStarterItems_still_surfaces_a_completion_kept_after_an_unrelated_undo()
    {
        // The other side of the same rule: an undo elsewhere in the sequence must not
        // blind the scan to a completion the player legitimately bought and kept.
        var starterItems = StarterItemAnalyzer.BuildStarterItems(
            [
                new ItemEvent { TimestampMs = 2_000, ItemId = 1001, EventType = "ITEM_PURCHASED" },
                new ItemEvent { TimestampMs = 2_500, BeforeId = 1001, EventType = "ITEM_UNDO" },
                new ItemEvent { TimestampMs = 410_000, ItemId = 3865, EventType = "ITEM_DESTROYED" },
                new ItemEvent { TimestampMs = 800_000, ItemId = 3877, EventType = "ITEM_PURCHASED" }
            ],
            Metadata);

        starterItems.Should().Contain(3877);
    }
}
