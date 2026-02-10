using System.Text.Json.Serialization;

namespace Ingestor.Riot.Dto;

public class RiotSummonerDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("puuid")]
    public string Puuid { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("profileIconId")]
    public int ProfileIconId { get; set; }

    [JsonPropertyName("summonerLevel")]
    public long SummonerLevel { get; set; }
}
