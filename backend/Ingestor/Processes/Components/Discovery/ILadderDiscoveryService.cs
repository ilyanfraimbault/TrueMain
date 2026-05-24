using Core.Lol.Identifiers;
using Ingestor.Options;
using Ingestor.Ranking;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.Discovery;

public sealed record DiscoveredSummoner(RiotSummonerDto Summoner, RankSnapshotInput? Rank);

public interface ILadderDiscoveryService
{
    Task<List<DiscoveredSummoner>> DiscoverSummonersAsync(
        PlatformRoute platform,
        DiscoveryOptions options,
        CancellationToken ct);
}
