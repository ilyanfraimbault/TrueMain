using System.Text.Json.Serialization;

namespace Ingestor.Riot.Dto;

public sealed record RiotAccountDto
{
    [JsonPropertyName("puuid")]
    public string Puuid { get; init; } = string.Empty;

    [JsonPropertyName("gameName")]
    public string? GameName { get; init; }

    [JsonPropertyName("tagLine")]
    public string? TagLine { get; init; }
}
