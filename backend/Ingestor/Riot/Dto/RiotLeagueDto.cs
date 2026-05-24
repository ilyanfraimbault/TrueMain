using System.Text.Json.Serialization;

namespace Ingestor.Riot.Dto;

public class RiotLeagueListDto
{
    [JsonPropertyName("tier")]
    public string? Tier { get; set; }

    [JsonPropertyName("entries")]
    public List<RiotLeagueEntryDto> Entries { get; set; } = new();
}

public class RiotLeagueEntryDto
{
    [JsonPropertyName("summonerId")]
    public string? SummonerId { get; set; }

    [JsonPropertyName("puuid")]
    public string? Puuid { get; set; }

    [JsonPropertyName("rank")]
    public string? Rank { get; set; }

    [JsonPropertyName("leaguePoints")]
    public int LeaguePoints { get; set; }

    [JsonPropertyName("wins")]
    public int Wins { get; set; }

    [JsonPropertyName("losses")]
    public int Losses { get; set; }
}
