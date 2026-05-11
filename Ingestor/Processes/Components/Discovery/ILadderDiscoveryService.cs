using Core.Lol.Identifiers;
using Ingestor.Options;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.Discovery;

public interface ILadderDiscoveryService
{
    Task<List<RiotSummonerDto>> DiscoverSummonersAsync(
        PlatformRoute platform,
        DiscoveryOptions options,
        CancellationToken ct);
}
