using Core;
using Core.Lol.Identifiers;
using Ingestor.Riot.Dto;

namespace Ingestor.Riot;

public sealed class RiotPlatformClient : IRiotPlatformClient
{
    private readonly HttpClient _httpClient;

    public RiotPlatformClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<RiotLeagueListDto> GetChallengerLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
    {
        return GetAsync<RiotLeagueListDto>(RiotEndpoint.ChallengerLeague, platform, $"/lol/league/v4/challengerleagues/by-queue/{queue}", ct);
    }

    public Task<RiotLeagueListDto> GetGrandmasterLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
    {
        return GetAsync<RiotLeagueListDto>(RiotEndpoint.GrandmasterLeague, platform, $"/lol/league/v4/grandmasterleagues/by-queue/{queue}", ct);
    }

    public Task<RiotLeagueListDto> GetMasterLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
    {
        return GetAsync<RiotLeagueListDto>(RiotEndpoint.MasterLeague, platform, $"/lol/league/v4/masterleagues/by-queue/{queue}", ct);
    }

    public Task<RiotSummonerDto> GetSummonerAsync(PlatformRoute platform, string summonerId, CancellationToken ct)
    {
        return GetAsync<RiotSummonerDto>(RiotEndpoint.SummonerById, platform, $"/lol/summoner/v4/summoners/{summonerId}", ct);
    }

    public Task<RiotSummonerDto> GetSummonerByPuuidAsync(PlatformRoute platform, string puuid, CancellationToken ct)
    {
        return GetAsync<RiotSummonerDto>(RiotEndpoint.SummonerByPuuid, platform, $"/lol/summoner/v4/summoners/by-puuid/{puuid}", ct);
    }

    public Task<List<RiotChampionMasteryDto>> GetChampionMasteriesAsync(PlatformRoute platform, string puuid, CancellationToken ct)
    {
        return GetAsync<List<RiotChampionMasteryDto>>(RiotEndpoint.ChampionMasteriesByPuuid, platform, $"/lol/champion-mastery/v4/champion-masteries/by-puuid/{puuid}", ct);
    }

    public Task<List<RiotLeagueEntryByPuuidDto>> GetLeagueEntriesByPuuidAsync(PlatformRoute platform, string puuid, CancellationToken ct)
    {
        return GetAsync<List<RiotLeagueEntryByPuuidDto>>(RiotEndpoint.LeagueEntriesByPuuid, platform, $"/lol/league/v4/entries/by-puuid/{puuid}", ct);
    }

    public Task<List<RiotLeagueDivisionEntryDto>> GetLeagueEntriesAsync(
        PlatformRoute platform,
        string queue,
        string tier,
        string division,
        int page,
        CancellationToken ct)
    {
        return GetAsync<List<RiotLeagueDivisionEntryDto>>(
            RiotEndpoint.LeagueEntriesByDivision,
            platform,
            $"/lol/league/v4/entries/{queue}/{tier}/{division}?page={page}",
            ct);
    }

    private Task<T> GetAsync<T>(RiotEndpoint endpoint, PlatformRoute platform, string pathAndQuery, CancellationToken ct)
        => _httpClient.GetFromJsonStreamingAsync<T>(RiotCall.Get(endpoint, platform.ToPlatformHost(), pathAndQuery), ct);
}
