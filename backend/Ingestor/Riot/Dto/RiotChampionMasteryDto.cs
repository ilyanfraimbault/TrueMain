using System.Text.Json.Serialization;

namespace Ingestor.Riot.Dto;

public sealed record RiotChampionMasteryDto
{
    [JsonPropertyName("championId")]
    public int ChampionId { get; init; }

    [JsonPropertyName("championPoints")]
    public long ChampionPoints { get; init; }

    [JsonPropertyName("lastPlayTime")]
    public long LastPlayTime { get; init; }
}
