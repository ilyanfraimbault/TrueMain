using AwesomeAssertions;
using Core.Lol.Items;
using Data.BuildFacts;
using Data.Entities;
using TrueMain.UnitTests.Fixtures;

namespace TrueMain.UnitTests;

public sealed class RoleBoundBootsInferenceTests
{
    private const int Greaves = LolItemIds.TierTwoBoots.BerserkersGreaves;

    private static IReadOnlyDictionary<int, ItemMetadata> Metadata => ItemMetadataFixtures.ItemMetadataById;

    private static ItemEvent Event(int timestampMs, string eventType, int itemId, int? beforeId = null)
        => new() { TimestampMs = timestampMs, EventType = eventType, ItemId = itemId, BeforeId = beforeId };

    [Fact]
    public void Infer_ReturnsTheLastBootsBought()
    {
        var boots = RoleBoundBootsInference.Infer(
        [
            Event(60_000, ItemEventTypes.Purchased, LolItemIds.BootsOfSpeed),
            Event(400_000, ItemEventTypes.Purchased, Greaves),
            Event(400_000, ItemEventTypes.Destroyed, LolItemIds.BootsOfSpeed)
        ], [3153, 3031, 0, 0, 0, 0], Metadata);

        boots.Should().Be(Greaves);
    }

    [Fact]
    public void Infer_ReturnsNothingWhenTheInventoryAlreadyHoldsBoots()
    {
        var boots = RoleBoundBootsInference.Infer(
            [Event(400_000, ItemEventTypes.Purchased, Greaves)],
            [3153, Greaves, 0, 0, 0, 0],
            Metadata);

        boots.Should().Be(0);
    }

    [Fact]
    public void Infer_DropsAnUndonePurchase()
    {
        var boots = RoleBoundBootsInference.Infer(
        [
            Event(60_000, ItemEventTypes.Purchased, LolItemIds.BootsOfSpeed),
            Event(400_000, ItemEventTypes.Purchased, Greaves),
            Event(401_000, ItemEventTypes.Undo, 0, beforeId: Greaves)
        ], [3153, 0, 0, 0, 0, 0], Metadata);

        boots.Should().Be(LolItemIds.BootsOfSpeed);
    }

    [Fact]
    public void Infer_FallsBackToBootsThePlayerNeverBought()
    {
        // Slightly Magical Footwear: granted by a rune, so the timeline only ever
        // destroys it when it upgrades.
        var boots = RoleBoundBootsInference.Infer(
            [Event(700_000, ItemEventTypes.Destroyed, LolItemIds.BootsOfSpeed)],
            [3153, 0, 0, 0, 0, 0],
            Metadata);

        boots.Should().Be(LolItemIds.BootsOfSpeed);
    }

    [Fact]
    public void Infer_ReturnsNothingWhenTheTimelineNamesNoBoots()
    {
        var boots = RoleBoundBootsInference.Infer(
            [Event(600_000, ItemEventTypes.Purchased, 3153)],
            [3153, 0, 0, 0, 0, 0],
            Metadata);

        boots.Should().Be(0);
    }
}
