using System.Text.Json.Serialization;

namespace Ingestor.Riot.Dto;

/// <summary>
/// One entry of the paginated <c>league-v4/entries/{queue}/{tier}/{division}</c> ladder.
/// </summary>
/// <remarks>
/// Riot's <c>LeagueEntryDTO</c>, the same wire shape <see cref="RiotLeagueEntryByPuuidDto"/>
/// reads — kept as its own type because the two are consumed for opposite reasons: there the
/// account is known and the tier is the answer, here the tier is known and the account is the
/// answer, so this one needs <see cref="Puuid"/> and that one does not. Distinct from
/// <see cref="RiotLeagueEntryDto"/>, the apex <c>LeagueItemDTO</c>, which carries no tier of
/// its own because its parent league list holds it.
/// </remarks>
public sealed record RiotLeagueDivisionEntryDto
{
    [JsonPropertyName("puuid")]
    public string? Puuid { get; init; }

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
