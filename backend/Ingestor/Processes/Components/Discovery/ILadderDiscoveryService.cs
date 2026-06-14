using Core.Lol.Identifiers;
using Ingestor.Options;
using Ingestor.Ranking;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.Discovery;

public sealed record DiscoveredSummoner(RiotSummonerDto Summoner, RankSnapshotInput? Rank);

/// <summary>
/// Result of a ladder discovery slice: the resolved summoners for the selected
/// window, plus the distinct ladder size and the offset actually applied (after
/// clamping) so the caller can advance and wrap the per-platform cursor (#486).
/// </summary>
public sealed record LadderDiscoveryResult(
    List<DiscoveredSummoner> Discovered,
    int LadderSize,
    int AppliedOffset);

public interface ILadderDiscoveryService
{
    Task<LadderDiscoveryResult> DiscoverSummonersAsync(
        PlatformRoute platform,
        DiscoveryOptions options,
        int offset,
        CancellationToken ct);
}
