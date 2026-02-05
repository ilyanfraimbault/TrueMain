using Core;
using Ingestor.Riot.Dto;

namespace Ingestor.Riot;

public interface IRiotMatchClient
{
    Task<RiotMatchDto> GetMatchAsync(string matchId, RegionalRoute region, CancellationToken ct);
    Task<RiotTimelineDto> GetTimelineAsync(string matchId, RegionalRoute region, CancellationToken ct);
}
