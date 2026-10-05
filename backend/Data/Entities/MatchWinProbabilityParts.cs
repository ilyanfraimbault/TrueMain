using System.Text.Json.Serialization;

namespace Data.Entities;

// The jsonb parts of MatchWinProbability (no "Document" suffix: that one marks the
// Mongo documents, MongoDocumentFieldNamingTests). As for ItemEvent, every property pins its
// key with [JsonPropertyName] so a rename cannot silently turn the stored rows into defaults.

/// <summary>One point of the curve: team 100's chance at a game time.</summary>
public class MatchWinProbabilityPoint
{
    [JsonPropertyName("Ms")]
    public int Ms { get; set; }

    [JsonPropertyName("P")]
    public double P { get; set; }
}

/// <summary>One turning point; see <c>Core.Lol.WinProbability.WinProbabilitySwing</c>.</summary>
public class MatchWinProbabilitySwing
{
    [JsonPropertyName("Ms")]
    public int Ms { get; set; }

    [JsonPropertyName("Kind")]
    public string Kind { get; set; } = string.Empty;

    [JsonPropertyName("TeamId")]
    public int TeamId { get; set; }

    [JsonPropertyName("Delta")]
    public double Delta { get; set; }

    [JsonPropertyName("KillerId")]
    public int KillerId { get; set; }

    [JsonPropertyName("VictimId")]
    public int VictimId { get; set; }

    [JsonPropertyName("Assists")]
    public int Assists { get; set; }

    [JsonPropertyName("Bounty")]
    public int? Bounty { get; set; }

    [JsonPropertyName("Lane")]
    public string? Lane { get; set; }

    [JsonPropertyName("TowerType")]
    public string? TowerType { get; set; }

    [JsonPropertyName("MonsterSubType")]
    public string? MonsterSubType { get; set; }
}

/// <summary>One epic monster taken; see <c>Core.Lol.WinProbability.WinProbabilityObjective</c>.</summary>
public class MatchWinProbabilityObjective
{
    [JsonPropertyName("Ms")]
    public int Ms { get; set; }

    [JsonPropertyName("MonsterType")]
    public string MonsterType { get; set; } = string.Empty;

    [JsonPropertyName("MonsterSubType")]
    public string? MonsterSubType { get; set; }

    [JsonPropertyName("TeamId")]
    public int TeamId { get; set; }

    /// <summary>Null for a monster the model does not weigh.</summary>
    [JsonPropertyName("Delta")]
    public double? Delta { get; set; }
}
