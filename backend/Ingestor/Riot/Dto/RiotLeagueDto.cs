using System.Text.Json.Serialization;

namespace Ingestor.Riot.Dto;

public sealed record RiotLeagueListDto
{
    [JsonPropertyName("tier")]
    public string? Tier { get; init; }

    [JsonPropertyName("entries")]
    public IReadOnlyList<RiotLeagueEntryDto> Entries { get; init; } = [];
}

public sealed record RiotLeagueEntryDto
{
    [JsonPropertyName("summonerId")]
    public string? SummonerId { get; init; }

    [JsonPropertyName("puuid")]
    public string? Puuid { get; init; }

    [JsonPropertyName("rank")]
    public string? Rank { get; init; }

    [JsonPropertyName("leaguePoints")]
    public int LeaguePoints { get; init; }

    [JsonPropertyName("wins")]
    public int Wins { get; init; }

    [JsonPropertyName("losses")]
    public int Losses { get; init; }
}
