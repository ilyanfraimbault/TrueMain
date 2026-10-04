using System.Text.Json.Serialization;

namespace Ingestor.Riot.Dto;

public sealed record RiotSummonerDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("puuid")]
    public string Puuid { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("profileIconId")]
    public int ProfileIconId { get; init; }

    [JsonPropertyName("summonerLevel")]
    public long SummonerLevel { get; init; }
}
