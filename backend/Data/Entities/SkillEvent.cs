using System.Text.Json.Serialization;

namespace Data.Entities;

/// <summary>
/// One skill level-up of a <see cref="MatchParticipant"/>, persisted inside the
/// <c>SkillEvents</c> <c>jsonb</c> column by Npgsql dynamic JSON.
/// </summary>
/// <remarks>
/// As for <see cref="ItemEvent"/>, the <see cref="JsonPropertyNameAttribute"/> on
/// every property pins the wire shape of the stored documents to the PascalCase
/// keys already written in production
/// (<c>{"SkillSlot":…,"LevelUpType":…,"TimestampMs":…}</c>), so a property rename or
/// a serializer naming policy cannot silently orphan the existing rows.
/// </remarks>
public class SkillEvent
{
    /// <summary>Milliseconds since the start of the game.</summary>
    [JsonPropertyName("TimestampMs")]
    public int TimestampMs { get; set; }

    /// <summary>Riot skill slot: <c>1</c> = Q, <c>2</c> = W, <c>3</c> = E, <c>4</c> = R.</summary>
    [JsonPropertyName("SkillSlot")]
    public int SkillSlot { get; set; }

    /// <summary>Riot level-up type, for example <c>NORMAL</c> or <c>EVOLVE</c>.</summary>
    [JsonPropertyName("LevelUpType")]
    public string LevelUpType { get; set; } = string.Empty;
}
