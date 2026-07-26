using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Data.Entities;

namespace TrueMain.UnitTests;

/// <summary>
/// Guards the on-disk wire shape of the <c>match_participants.ItemEvents</c> and
/// <c>match_participants.SkillEvents</c> <c>jsonb</c> columns.
/// </summary>
/// <remarks>
/// Those columns are written and read by Npgsql dynamic JSON with the default
/// serializer options (no naming policy is configured on the data source built in
/// <c>DataServiceCollectionExtensions.BuildDataSource</c>), so the documents already
/// stored in production use PascalCase keys. The payloads below are copies of real
/// stored rows. If a future rename or serializer configuration breaks that shape,
/// existing rows would silently deserialize to defaults — these tests fail instead.
/// </remarks>
public sealed class MatchParticipantEventJsonShapeTests
{
    // Verbatim copies of stored documents, keys and ordering included.
    private const string StoredItemEventJson =
        """{"ItemId": 3340, "AfterId": null, "BeforeId": null, "EventType": "ITEM_PURCHASED", "TimestampMs": 4122}""";

    private const string StoredSkillEventJson =
        """{"SkillSlot": 2, "LevelUpType": "NORMAL", "TimestampMs": 77582}""";

    private const string StoredItemUndoEventJson =
        """{"ItemId": 0, "AfterId": 1055, "BeforeId": 1036, "EventType": "ITEM_UNDO", "TimestampMs": 61234}""";

    [Fact]
    public void Stored_item_event_document_still_deserializes()
    {
        var itemEvent = JsonSerializer.Deserialize<ItemEvent>(StoredItemEventJson);

        itemEvent.Should().NotBeNull();
        itemEvent!.ItemId.Should().Be(3340);
        itemEvent.EventType.Should().Be("ITEM_PURCHASED");
        itemEvent.TimestampMs.Should().Be(4122);
        itemEvent.BeforeId.Should().BeNull();
        itemEvent.AfterId.Should().BeNull();
    }

    [Fact]
    public void Stored_item_undo_document_still_deserializes()
    {
        var itemEvent = JsonSerializer.Deserialize<ItemEvent>(StoredItemUndoEventJson);

        itemEvent.Should().NotBeNull();
        itemEvent!.ItemId.Should().Be(0);
        itemEvent.EventType.Should().Be("ITEM_UNDO");
        itemEvent.TimestampMs.Should().Be(61234);
        itemEvent.BeforeId.Should().Be(1036);
        itemEvent.AfterId.Should().Be(1055);
    }

    [Fact]
    public void Stored_skill_event_document_still_deserializes()
    {
        var skillEvent = JsonSerializer.Deserialize<SkillEvent>(StoredSkillEventJson);

        skillEvent.Should().NotBeNull();
        skillEvent!.SkillSlot.Should().Be(2);
        skillEvent.LevelUpType.Should().Be("NORMAL");
        skillEvent.TimestampMs.Should().Be(77582);
    }

    [Fact]
    public void Item_event_round_trips_through_the_stored_shape()
    {
        var original = new ItemEvent
        {
            TimestampMs = 4122,
            EventType = "ITEM_PURCHASED",
            ItemId = 3340,
            BeforeId = null,
            AfterId = null
        };

        var roundTripped = JsonSerializer.Deserialize<ItemEvent>(JsonSerializer.Serialize(original));

        roundTripped.Should().NotBeNull();
        roundTripped!.TimestampMs.Should().Be(original.TimestampMs);
        roundTripped.EventType.Should().Be(original.EventType);
        roundTripped.ItemId.Should().Be(original.ItemId);
        roundTripped.BeforeId.Should().Be(original.BeforeId);
        roundTripped.AfterId.Should().Be(original.AfterId);
    }

    [Fact]
    public void Skill_event_round_trips_through_the_stored_shape()
    {
        var original = new SkillEvent
        {
            TimestampMs = 77582,
            SkillSlot = 2,
            LevelUpType = "NORMAL"
        };

        var roundTripped = JsonSerializer.Deserialize<SkillEvent>(JsonSerializer.Serialize(original));

        roundTripped.Should().NotBeNull();
        roundTripped!.TimestampMs.Should().Be(original.TimestampMs);
        roundTripped.SkillSlot.Should().Be(original.SkillSlot);
        roundTripped.LevelUpType.Should().Be(original.LevelUpType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("camelCase")]
    [InlineData("snake_case")]
    public void Item_event_keeps_its_stored_keys_under_any_naming_policy(string? policy)
    {
        var json = JsonSerializer.Serialize(
            new ItemEvent { TimestampMs = 4122, EventType = "ITEM_PURCHASED", ItemId = 3340 },
            OptionsWith(policy));

        KeysOf(json).Should().BeEquivalentTo("TimestampMs", "EventType", "ItemId", "BeforeId", "AfterId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("camelCase")]
    [InlineData("snake_case")]
    public void Skill_event_keeps_its_stored_keys_under_any_naming_policy(string? policy)
    {
        var json = JsonSerializer.Serialize(
            new SkillEvent { TimestampMs = 77582, SkillSlot = 2, LevelUpType = "NORMAL" },
            OptionsWith(policy));

        KeysOf(json).Should().BeEquivalentTo("TimestampMs", "SkillSlot", "LevelUpType");
    }

    [Theory]
    [InlineData("camelCase")]
    [InlineData("snake_case")]
    public void Stored_documents_still_deserialize_under_any_naming_policy(string policy)
    {
        var options = OptionsWith(policy);

        var itemEvent = JsonSerializer.Deserialize<ItemEvent>(StoredItemEventJson, options);
        var skillEvent = JsonSerializer.Deserialize<SkillEvent>(StoredSkillEventJson, options);

        itemEvent.Should().NotBeNull();
        itemEvent!.ItemId.Should().Be(3340);
        itemEvent.EventType.Should().Be("ITEM_PURCHASED");
        itemEvent.TimestampMs.Should().Be(4122);

        skillEvent.Should().NotBeNull();
        skillEvent!.SkillSlot.Should().Be(2);
        skillEvent.LevelUpType.Should().Be("NORMAL");
        skillEvent.TimestampMs.Should().Be(77582);
    }

    private static JsonSerializerOptions? OptionsWith(string? policy) => policy switch
    {
        "camelCase" => new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase },
        "snake_case" => new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower },
        _ => null
    };

    private static IEnumerable<string> KeysOf(string json) =>
        JsonNode.Parse(json)!.AsObject().Select(property => property.Key);
}
