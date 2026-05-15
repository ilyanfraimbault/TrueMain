using System.Text.Json.Serialization;

namespace Ingestor.Riot.Dto;

public class RiotChampionMasteryDto
{
    [JsonPropertyName("championId")]
    public int ChampionId { get; set; }

    [JsonPropertyName("championPoints")]
    public long ChampionPoints { get; set; }

    [JsonPropertyName("lastPlayTime")]
    public long LastPlayTime { get; set; }
}
