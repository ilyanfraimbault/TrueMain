using Core.Lol.Identifiers;
using Ingestor.Riot.Dto;

namespace Ingestor.Riot;

public interface IRiotMatchClient
{
    Task<RiotMatchDto> GetMatchAsync(string matchId, RegionalRoute region, CancellationToken ct);
    Task<MatchTimelineDto> GetTimelineAsync(string matchId, RegionalRoute region, CancellationToken ct);
    Task<List<string>> GetMatchIdsAsync(string puuid, RegionalRoute region, int count, CancellationToken ct);
}
