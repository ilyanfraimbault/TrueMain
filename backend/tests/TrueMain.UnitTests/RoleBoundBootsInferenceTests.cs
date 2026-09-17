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
    public void Infer_returns_the_last_boots_bought()
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
    public void Infer_returns_nothing_when_the_inventory_already_holds_boots()
    {
        var boots = RoleBoundBootsInference.Infer(
            [Event(400_000, ItemEventTypes.Purchased, Greaves)],
            [3153, Greaves, 0, 0, 0, 0],
            Metadata);

        boots.Should().Be(0);
    }

    [Fact]
    public void Infer_drops_an_undone_purchase()
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
    public void Infer_falls_back_to_boots_the_player_never_bought()
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
    public void Infer_returns_nothing_when_the_timeline_names_no_boots()
    {
        var boots = RoleBoundBootsInference.Infer(
            [Event(600_000, ItemEventTypes.Purchased, 3153)],
            [3153, 0, 0, 0, 0, 0],
            Metadata);

        boots.Should().Be(0);
    }
}
