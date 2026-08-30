using Core.Lol.Identifiers;
using Ingestor.Riot.Dto;

namespace Ingestor.Riot;

public interface IRiotPlatformClient
{
    Task<RiotLeagueListDto> GetChallengerLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct);
    Task<RiotLeagueListDto> GetGrandmasterLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct);
    Task<RiotLeagueListDto> GetMasterLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct);
    Task<RiotSummonerDto> GetSummonerAsync(PlatformRoute platform, string summonerId, CancellationToken ct);
    Task<RiotSummonerDto> GetSummonerByPuuidAsync(PlatformRoute platform, string puuid, CancellationToken ct);
    Task<List<RiotChampionMasteryDto>> GetChampionMasteriesAsync(PlatformRoute platform, string puuid, CancellationToken ct);
    Task<List<RiotLeagueEntryByPuuidDto>> GetLeagueEntriesByPuuidAsync(PlatformRoute platform, string puuid, CancellationToken ct);

    /// <summary>
    /// One page of the ranked ladder for a (tier, division) below Master. Riot pages this
    /// endpoint 1-based and answers a page past the end with an empty array, which is the
    /// sweep's end-of-division signal (#1312).
    /// </summary>
    Task<List<RiotLeagueDivisionEntryDto>> GetLeagueEntriesAsync(
        PlatformRoute platform,
        string queue,
        string tier,
        string division,
        int page,
        CancellationToken ct);
}
