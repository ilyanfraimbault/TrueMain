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
        var uri = BuildPlatformUri(platform, $"/lol/league/v4/challengerleagues/by-queue/{queue}");
        return GetAsync<RiotLeagueListDto>(uri, ct);
    }

    public Task<RiotLeagueListDto> GetGrandmasterLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
    {
        var uri = BuildPlatformUri(platform, $"/lol/league/v4/grandmasterleagues/by-queue/{queue}");
        return GetAsync<RiotLeagueListDto>(uri, ct);
    }

    public Task<RiotLeagueListDto> GetMasterLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
    {
        var uri = BuildPlatformUri(platform, $"/lol/league/v4/masterleagues/by-queue/{queue}");
        return GetAsync<RiotLeagueListDto>(uri, ct);
    }

    public Task<RiotSummonerDto> GetSummonerAsync(PlatformRoute platform, string summonerId, CancellationToken ct)
    {
        var uri = BuildPlatformUri(platform, $"/lol/summoner/v4/summoners/{summonerId}");
        return GetAsync<RiotSummonerDto>(uri, ct);
    }

    public Task<RiotSummonerDto> GetSummonerByPuuidAsync(PlatformRoute platform, string puuid, CancellationToken ct)
    {
        var uri = BuildPlatformUri(platform, $"/lol/summoner/v4/summoners/by-puuid/{puuid}");
        return GetAsync<RiotSummonerDto>(uri, ct);
    }

    public Task<List<RiotChampionMasteryDto>> GetChampionMasteriesAsync(PlatformRoute platform, string puuid, CancellationToken ct)
    {
        var uri = BuildPlatformUri(platform, $"/lol/champion-mastery/v4/champion-masteries/by-puuid/{puuid}");
        return GetAsync<List<RiotChampionMasteryDto>>(uri, ct);
    }

    public Task<List<RiotLeagueEntryByPuuidDto>> GetLeagueEntriesByPuuidAsync(PlatformRoute platform, string puuid, CancellationToken ct)
    {
        var uri = BuildPlatformUri(platform, $"/lol/league/v4/entries/by-puuid/{puuid}");
        return GetAsync<List<RiotLeagueEntryByPuuidDto>>(uri, ct);
    }

    private Task<T> GetAsync<T>(Uri uri, CancellationToken ct)
    {
        return _httpClient.GetFromJsonStreamingAsync<T>(uri, ct);
    }

    private static Uri BuildPlatformUri(PlatformRoute platform, string path)
    {
        var host = platform.ToPlatformHost();
        return new Uri($"https://{host}.api.riotgames.com{path}");
    }
}
