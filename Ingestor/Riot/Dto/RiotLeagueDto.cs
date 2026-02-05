using System.Text.Json.Serialization;

namespace Ingestor.Riot.Dto;

public class RiotLeagueListDto
{
    [JsonPropertyName("entries")]
    public List<RiotLeagueEntryDto> Entries { get; set; } = new();
}

public class RiotLeagueEntryDto
{
    [JsonPropertyName("summonerId")]
    public string? SummonerId { get; set; }

    [JsonPropertyName("puuid")]
    public string? Puuid { get; set; }
}
