using System.Text.Json.Serialization;

namespace Ingestor.Riot.Dto;

public sealed record RiotLeagueEntryByPuuidDto
{
    [JsonPropertyName("queueType")]
    public string? QueueType { get; init; }

    [JsonPropertyName("tier")]
    public string? Tier { get; init; }

    [JsonPropertyName("rank")]
    public string? Rank { get; init; }

    [JsonPropertyName("leaguePoints")]
    public int LeaguePoints { get; init; }

    [JsonPropertyName("wins")]
    public int Wins { get; init; }

    [JsonPropertyName("losses")]
    public int Losses { get; init; }
}
