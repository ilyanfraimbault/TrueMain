using Core.Lol.Items;
using Data.BuildFacts;
using Data.Entities;
using AwesomeAssertions;
using TrueMain.UnitTests.Fixtures;

namespace TrueMain.UnitTests;

public sealed class BootsResolverTests
{
    private static IReadOnlyDictionary<int, ItemMetadata> Metadata => ItemMetadataFixtures.ItemMetadataById;

    [Fact]
    public void Resolve_ReturnsTheMostRelevantBootsFromFinalInventory()
    {
        var bootsItemId = BootsResolver.Resolve(
            [new ItemEvent { TimestampMs = 5_000, ItemId = 3006, EventType = "ITEM_PURCHASED" }],
            [3153, 1001, 3006, 3031, 0, 0, 0], [], Metadata);

        bootsItemId.Should().Be(3006);
    }

    [Fact]
    public void Resolve_UsesPurchasedBootsEvenWhenTheyAreMissingFromFinalInventory()
    {
        var bootsItemId = BootsResolver.Resolve(
        [
            new ItemEvent { TimestampMs = 120_000, ItemId = 3006, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 720_000, ItemId = 3006, EventType = "ITEM_DESTROYED" }
        ], [3153, 3124, 3302, 3082, 0, 0, 0], [], Metadata);

        bootsItemId.Should().Be(3006);
    }

    [Fact]
    public void Resolve_IgnoresUndoneBootPurchases()
    {
        var bootsItemId = BootsResolver.Resolve(
        [
            new ItemEvent { TimestampMs = 120_000, ItemId = 3006, EventType = "ITEM_PURCHASED" },
            new ItemEvent { TimestampMs = 121_000, ItemId = 3006, BeforeId = 3006, EventType = "ITEM_UNDO" }
        ], [3153, 3124, 3302, 3082, 0, 0, 0], [], Metadata);

        bootsItemId.Should().Be(0);
    }

    [Fact]
    public void Resolve_FindsBootsHeldInTheRoleBoundSlotOfAFullInventory()
    {
        var bootsItemId = BootsResolver.Resolve(
            [],
            FinalInventory.Of(3153, 3031, 3085, 6672, 3072, LolItemIds.Manamune, LolItemIds.TierTwoBoots.BerserkersGreaves),
            [],
            Metadata);

        bootsItemId.Should().Be(LolItemIds.TierTwoBoots.BerserkersGreaves);
    }
}
