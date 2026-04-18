using Core.Lol.Items;
using Data.Entities;
using FluentAssertions;
using Ingestor.Processes.Components.PatternAggregation;

namespace TrueMain.UnitTests;

public sealed class ChampionPatternNormalizationTests
{
    private static readonly IReadOnlyDictionary<int, ItemMetadata> ItemMetadataById = new Dictionary<int, ItemMetadata>
    {
        [LolItemIds.Trinkets.StealthWard] = new(LolItemIds.Trinkets.StealthWard, 0, true, false, false, false, true, false),
        [2003] = new(2003, 50, true, true, false, false, true, false),
        [LolItemIds.Cull] = new(LolItemIds.Cull, 450, true, false, false, false, true, false),
        [1055] = new(1055, 450, true, false, false, false, false, false),
        [1056] = new(1056, 400, true, false, false, false, false, false),
        [LolItemIds.BootsOfSpeed] = new(LolItemIds.BootsOfSpeed, 300, true, false, true, true, false, false),
        [LolItemIds.TearOfTheGoddess] = new(LolItemIds.TearOfTheGoddess, 400, true, false, false, false, false, false),
        [LolItemIds.SupportQuest.SpellthiefsEdge] = new(LolItemIds.SupportQuest.SpellthiefsEdge, 400, true, false, false, false, false, false),
        [LolItemIds.SupportQuest.RelicShield] = new(LolItemIds.SupportQuest.RelicShield, 400, true, false, false, false, false, false),
        [LolItemIds.SupportQuest.SteelShoulderguards] = new(LolItemIds.SupportQuest.SteelShoulderguards, 400, true, false, false, false, false, false),
        [LolItemIds.TierTwoBoots.BerserkersGreaves] = new(LolItemIds.TierTwoBoots.BerserkersGreaves, 1100, true, false, true, false, true, true),
        [LolItemIds.Manamune] = new(LolItemIds.Manamune, 2900, true, false, false, false, true, false),
        [LolItemIds.Muramana] = new(LolItemIds.Muramana, 2900, false, false, false, false, true, false)
        {
            IsInventoryTransformItem = true,
            TransformFromItemId = LolItemIds.Manamune
        },
        [3031] = new(3031, 3000, true, false, false, false, true, false),
        [3085] = new(3085, 3000, true, false, false, false, true, false),
        [3153] = new(3153, 3200, true, false, false, false, true, false),
        [6672] = new(6672, 3000, true, false, false, false, true, false)
    };

    [Fact]
    public void BuildSkillOrderKey_ShouldReflectTheOrderBasicSpellsReachTheirSecondPoint()
    {
        var key = ChampionPatternNormalization.BuildSkillOrderKey(
        [
            new SkillEvent { TimestampMs = 1_000, SkillSlot = 1, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 2_000, SkillSlot = 1, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 3_000, SkillSlot = 3, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 4_000, SkillSlot = 2, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 5_000, SkillSlot = 1, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 6_000, SkillSlot = 1, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 7_000, SkillSlot = 4, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 8_000, SkillSlot = 1, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 9_000, SkillSlot = 2, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 10_000, SkillSlot = 3, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 11_000, SkillSlot = 2, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 12_000, SkillSlot = 2, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 13_000, SkillSlot = 4, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 14_000, SkillSlot = 2, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 15_000, SkillSlot = 3, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 16_000, SkillSlot = 3, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 17_000, SkillSlot = 4, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 18_000, SkillSlot = 3, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 19_000, SkillSlot = 1, LevelUpType = "EVOLVE" }
        ]);

        key.Should().Be("Q-W-E");
    }

    [Fact]
    public void BuildSkillOrderKey_ShouldFallbackToRemainingSpellWhenOnlyTwoSpellsReachedSecondPoint()
    {
        var key = ChampionPatternNormalization.BuildSkillOrderKey(
        [
            new SkillEvent { TimestampMs = 1_000, SkillSlot = 1, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 2_000, SkillSlot = 1, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 3_000, SkillSlot = 2, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 4_000, SkillSlot = 3, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 5_000, SkillSlot = 1, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 6_000, SkillSlot = 4, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 7_000, SkillSlot = 2, LevelUpType = "NORMAL" }
        ]);

        key.Should().Be("Q-W-E");
    }

    [Fact]
    public void BuildSkillOrderKey_ShouldReturnEmpty_WhenThereAreNoNormalBasicSkillEvents()
    {
        var key = ChampionPatternNormalization.BuildSkillOrderKey(
        [
            new SkillEvent { TimestampMs = 1_000, SkillSlot = 4, LevelUpType = "NORMAL" },
            new SkillEvent { TimestampMs = 2_000, SkillSlot = 1, LevelUpType = "EVOLVE" },
            new SkillEvent { TimestampMs = 3_000, SkillSlot = 2, LevelUpType = "EVOLVE" }
        ]);

        key.Should().BeEmpty();
    }

    [Fact]
    public void BuildStarterItems_ShouldKeepEarlyPurchasesWithin500Gold()
    {
        var starterItems = ChampionPatternNormalization.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 1000, ItemId = 1055, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 1500, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 95_000, ItemId = 1001, EventType = "ITEM_PURCHASED" }
        ],
            ItemMetadataById);

        starterItems.Should().Equal(1055, 2003);
    }

    [Fact]
    public void BuildStarterItems_ShouldKeepTheValidSubset_WhenLaterEarlyPurchasesWouldExceed500Gold()
    {
        var starterItems = ChampionPatternNormalization.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 1000, ItemId = 1001, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 1500, ItemId = 1055, EventType = "ITEM_PURCHASED" }
        ],
            ItemMetadataById);

        starterItems.Should().Equal(1001);
    }

    [Fact]
    public void BuildStarterItems_ShouldRespectUndoAndKeepTheFinalStarterBasket()
    {
        var starterItems = ChampionPatternNormalization.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 1_000, ItemId = 1001, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 1_500, ItemId = 1001, BeforeId = 1001, EventType = "ITEM_UNDO" },
            new ItemEvent { TimestampMs = 2_000, ItemId = 1055, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 2_500, ItemId = 2003, EventType = "ITEM_PURCHASED" }
        ],
            ItemMetadataById);

        starterItems.Should().Equal(1055, 2003);
    }

    [Fact]
    public void BuildStarterItems_ShouldIgnoreStarterTrinketsInsteadOfDroppingTheWholeBasket()
    {
        var starterItems = ChampionPatternNormalization.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 1_000, ItemId = 3340, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 2_000, ItemId = 1055, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 3_000, ItemId = 2003, EventType = "ITEM_PURCHASED" }
        ],
            ItemMetadataById);

        starterItems.Should().Equal(1055, 2003);
    }

    [Fact]
    public void BuildStarterItems_ShouldKeepPurchasedPotionsEvenIfTheyAreConsumedBeforeTwoMinutes()
    {
        var starterItems = ChampionPatternNormalization.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 10_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 11_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 45_000, ItemId = 2003, EventType = "ITEM_DESTROYED" },
            new ItemEvent { TimestampMs = 66_000, ItemId = 2003, EventType = "ITEM_DESTROYED" }
        ],
            ItemMetadataById);

        starterItems.Should().Equal(2003, 2003);
    }

    [Fact]
    public void BuildStarterItems_ShouldIgnorePurchasesThatWouldPushTheStarterAbove500Gold()
    {
        var starterItems = ChampionPatternNormalization.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 1_000, ItemId = 1001, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 1_500, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 2_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 2_500, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 3_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 3_500, ItemId = 2003, EventType = "ITEM_PURCHASED" }
        ],
            ItemMetadataById);

        starterItems.Should().Equal(1001, 2003, 2003, 2003, 2003);
    }

    [Fact]
    public void BuildStarterItems_ShouldKeepTheCurrentValidStarterBasketWhenALaterPurchaseWouldOverflow()
    {
        var starterItems = ChampionPatternNormalization.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 1_000, ItemId = 3070, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 1_500, ItemId = 1056, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 2_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 2_500, ItemId = 2003, EventType = "ITEM_PURCHASED" }
        ],
            ItemMetadataById);

        starterItems.Should().Equal(3070, 2003, 2003);
    }

    [Fact]
    public void BuildStarterItems_ShouldIgnoreASecondShopBatchAfterALargeGap()
    {
        var starterItems = ChampionPatternNormalization.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 1_000, ItemId = 1055, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 2_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 50_000, ItemId = 3070, EventType = "ITEM_PURCHASED" }
        ],
            ItemMetadataById);

        starterItems.Should().Equal(1055, 2003);
    }

    [Fact]
    public void BuildStarterItems_ShouldInferSupportStarterWhenQuestChainExistsWithoutInitialPurchaseEvent()
    {
        var starterItems = ChampionPatternNormalization.BuildStarterItems(
        [
            new ItemEvent { TimestampMs = 3_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 3_200, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 3_400, ItemId = 3340, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 420_000, ItemId = 3865, EventType = "ITEM_DESTROYED" },
            new ItemEvent { TimestampMs = 794_000, ItemId = 3866, EventType = "ITEM_DESTROYED" },
            new ItemEvent { TimestampMs = 807_000, ItemId = 3867, EventType = "ITEM_DESTROYED" }
        ],
            ItemMetadataById);

        starterItems.Should().Equal(2003, 2003, 3865);
    }

    [Fact]
    public void AnalyzeStarterItems_ShouldNotCountInferredSupportStarterTowardPaidStarterCost()
    {
        var analysis = ChampionPatternNormalization.AnalyzeStarterItems(
        [
            new ItemEvent { TimestampMs = 3_000, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 3_200, ItemId = 2003, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 420_000, ItemId = 3865, EventType = "ITEM_DESTROYED" }
        ],
            ItemMetadataById);

        analysis.Items.Should().Equal(2003, 2003, 3865);
        analysis.TotalCost.Should().Be(100);
    }

    [Fact]
    public void BuildOrderedFinalBuild_ShouldKeepOnlyFinalCompletedItemsInCompletionOrder()
    {
        var buildItems = ChampionPatternNormalization.BuildOrderedFinalBuild(
        [
            new ItemEvent { TimestampMs = 1000, ItemId = 1055, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 5000, ItemId = 3006, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12000, ItemId = 3153, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 18000, ItemId = 3031, EventType = "ITEM_PURCHASED" }
        ],
            [3153, 3006, 3031, 1055, 0, 0],
            [1055],
            ItemMetadataById);

        buildItems.Should().Equal(3153, 3031);
    }

    [Fact]
    public void BuildOrderedFinalBuild_ShouldExcludeBaseBootsAndIntermediateItems()
    {
        var buildItems = ChampionPatternNormalization.BuildOrderedFinalBuild(
        [
            new ItemEvent { TimestampMs = 1000, ItemId = 1001, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 5000, ItemId = 3006, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12000, ItemId = 3085, EventType = "ITEM_PURCHASED" }
        ],
            [3006, 3085, 1055, 0, 0, 0],
            [1055],
            ItemMetadataById);

        buildItems.Should().Equal(3085);
    }

    [Fact]
    public void BuildOrderedFinalBuild_ShouldUseTheLatestReacquisitionTimeForFinalItems()
    {
        var buildItems = ChampionPatternNormalization.BuildOrderedFinalBuild(
        [
            new ItemEvent { TimestampMs = 5_000, ItemId = 3153, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 9_000, ItemId = 3153, EventType = "ITEM_SOLD" },
            new ItemEvent { TimestampMs = 12_000, ItemId = 3006, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 18_000, ItemId = 3153, EventType = "ITEM_PURCHASED" }
        ],
            [3153, 3006, 0, 0, 0, 0],
            [],
            ItemMetadataById);

        buildItems.Should().Equal(3153);
    }

    [Fact]
    public void BuildOrderedFinalBuild_ShouldIgnoreTrinketSlotItems()
    {
        var buildItems = ChampionPatternNormalization.BuildOrderedFinalBuild(
        [
            new ItemEvent { TimestampMs = 5_000, ItemId = 3153, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12_000, ItemId = 3006, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 18_000, ItemId = 3340, EventType = "ITEM_PURCHASED" }
        ],
            [3153, 3006, 3340, 0, 0, 0, 0],
            [],
            ItemMetadataById);

        buildItems.Should().Equal(3153);
    }

    [Fact]
    public void BuildOrderedFinalBuild_ShouldIgnoreCullInFinalBuild()
    {
        var buildItems = ChampionPatternNormalization.BuildOrderedFinalBuild(
        [
            new ItemEvent { TimestampMs = 5_000, ItemId = 1083, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12_000, ItemId = 3153, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 18_000, ItemId = 3031, EventType = "ITEM_PURCHASED" }
        ],
            [1083, 3153, 3031, 0, 0, 0, 0],
            [],
            ItemMetadataById);

        buildItems.Should().Equal(3153, 3031);
    }

    [Fact]
    public void BuildOrderedFinalBuild_ShouldIncludeInventoryTransformsUsingTheirSourcePurchaseTime()
    {
        var buildItems = ChampionPatternNormalization.BuildOrderedFinalBuild(
        [
            new ItemEvent { TimestampMs = 5_000, ItemId = 3004, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12_000, ItemId = 3153, EventType = "ITEM_PURCHASED" }
        ],
            [3042, 3153, 0, 0, 0, 0],
            [],
            ItemMetadataById);

        buildItems.Should().Equal(3004, 3153);
    }

    [Fact]
    public void BuildOrderedFinalBuild_ShouldDisplayTheSourceItemUsingTheTransformCompletionTime()
    {
        var buildItems = ChampionPatternNormalization.BuildOrderedFinalBuild(
        [
            new ItemEvent { TimestampMs = 5_000, ItemId = 3004, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 12_000, ItemId = 3153, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 20_000, ItemId = 3042, EventType = "ITEM_PURCHASED" }
        ],
            [3042, 3153, 0, 0, 0, 0],
            [],
            ItemMetadataById);

        buildItems.Should().Equal(3153, 3004);
    }

    [Fact]
    public void BuildCorrelatedBootsItem_ShouldReturnTheMostRelevantBootsFromFinalInventory()
    {
        var bootsItemId = ChampionPatternNormalization.BuildCorrelatedBootsItem(
            [
                new ItemEvent { TimestampMs = 5_000, ItemId = 3006, EventType = "ITEM_PURCHASED" }
            ],
            [3153, 1001, 3006, 3031, 0, 0, 0],
            [],
            ItemMetadataById);

        bootsItemId.Should().Be(3006);
    }

    [Fact]
    public void BuildCorrelatedBootsItem_ShouldUsePurchasedBootsEvenWhenTheyAreMissingFromFinalInventory()
    {
        var bootsItemId = ChampionPatternNormalization.BuildCorrelatedBootsItem(
        [
            new ItemEvent { TimestampMs = 120_000, ItemId = 3006, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 720_000, ItemId = 3006, EventType = "ITEM_DESTROYED" }
        ],
            [3153, 3124, 3302, 3082, 0, 0, 0],
            [],
            ItemMetadataById);

        bootsItemId.Should().Be(3006);
    }

    [Fact]
    public void BuildCorrelatedBootsItem_ShouldIgnoreUndoneBootPurchases()
    {
        var bootsItemId = ChampionPatternNormalization.BuildCorrelatedBootsItem(
        [
            new ItemEvent { TimestampMs = 120_000, ItemId = 3006, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 121_000, ItemId = 3006, BeforeId = 3006, EventType = "ITEM_UNDO" }
        ],
            [3153, 3124, 3302, 3082, 0, 0, 0],
            [],
            ItemMetadataById);

        bootsItemId.Should().Be(0);
    }

    [Fact]
    public void NormalizeTeamPosition_ShouldKeepOnlyValidTeamPositions()
    {
        ChampionPatternNormalization.NormalizeTeamPosition("BOTTOM").Should().Be("BOTTOM");
        ChampionPatternNormalization.NormalizeTeamPosition("UTILITY").Should().Be("UTILITY");
        ChampionPatternNormalization.NormalizeTeamPosition("INVALID").Should().BeNull();
        ChampionPatternNormalization.NormalizeTeamPosition("").Should().BeNull();
    }
}
