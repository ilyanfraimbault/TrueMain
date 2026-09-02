using Core.Lol.Identifiers;
using Ingestor.Options;
using Ingestor.Ranking;
using Ingestor.Riot.Dto;

namespace Ingestor.Processes.Components.Discovery;

/// <summary>
/// One ladder entry resolved into something the upsert can store.
/// <para>
/// <paramref name="ProfileResolved"/> is false when the summoner-v4 call was skipped because the
/// stored account is already fresh (#1358): <paramref name="Summoner"/> then carries only the
/// PUUID the ladder entry itself provided, so the upsert must leave the cosmetics — and the
/// profile-sync stamp — alone rather than overwriting them with blanks.
/// </para>
/// </summary>
public sealed record DiscoveredSummoner(
    RiotSummonerDto Summoner,
    RankSnapshotInput? Rank,
    bool ProfileResolved = true);

/// <summary>
/// Result of a ladder discovery slice: the resolved summoners for the selected
/// window, plus the distinct ladder size and the offset actually applied (after
/// clamping) so the caller can advance and wrap the per-platform cursor (#486), and how many
/// summoner-v4 calls the freshness gate saved (#1358).
/// </summary>
public sealed record LadderDiscoveryResult(
    List<DiscoveredSummoner> Discovered,
    int LadderSize,
    int AppliedOffset,
    int ProfileCallsSkipped);

/// <summary>
/// Answers, for the PUUIDs of one ladder window, which accounts are already stored with a
/// profile fresh enough that summoner-v4 would add nothing (#1358). A callback because the
/// window is only known after the ladder is fetched, and the crawl service itself owns no
/// database session.
/// </summary>
public delegate Task<IReadOnlySet<string>> ProfileFreshnessProbe(
    IReadOnlyCollection<string> puuids,
    CancellationToken ct);

public interface ILadderDiscoveryService
{
    Task<LadderDiscoveryResult> DiscoverSummonersAsync(
        PlatformRoute platform,
        DiscoveryOptions options,
        int offset,
        ProfileFreshnessProbe profileFreshnessProbe,
        CancellationToken ct);
}
