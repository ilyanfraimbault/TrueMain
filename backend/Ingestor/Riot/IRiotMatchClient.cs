using Core.Lol.Identifiers;
using Ingestor.Riot.Dto;

namespace Ingestor.Riot;

/// <summary>
/// Everything the match-ids endpoint is asked to narrow by (#1358). A record rather than a
/// growing positional parameter list, because every field here exists to stop the ingestor
/// paying for ids it will not store: a call that returns nothing storable is a bug, not a cost.
/// </summary>
/// <param name="Puuid">The account whose history is listed.</param>
/// <param name="Region">Regional route the account's matches live on.</param>
/// <param name="Count">
/// How many ids to ask for. Clamped to Riot's 1..100 range by the client, so a caller may raise
/// <c>MatchIngestion:MatchesPerAccount</c> to 100 without the endpoint 400-ing.
/// </param>
/// <param name="QueueId">
/// Riot queue id to filter on at the source (420 = ranked solo/duo). Combined with
/// <c>type=ranked</c> the two are ANDed, so flex (440) never reaches the per-match fetch.
/// </param>
/// <param name="StartTimeUtc">
/// Only list matches started at or after this instant. Riot honours <c>startTime</c> for matches
/// played after 2021-06-16 only — irrelevant for this pipeline, which never looks back that far.
/// </param>
public sealed record MatchIdQuery(
    string Puuid,
    RegionalRoute Region,
    int Count,
    int? QueueId = null,
    DateTime? StartTimeUtc = null);

public interface IRiotMatchClient
{
    Task<RiotMatchDto> GetMatchAsync(string matchId, RegionalRoute region, CancellationToken ct);
    Task<MatchTimelineDto> GetTimelineAsync(string matchId, RegionalRoute region, CancellationToken ct);
    Task<List<string>> GetMatchIdsAsync(MatchIdQuery query, CancellationToken ct);
}
