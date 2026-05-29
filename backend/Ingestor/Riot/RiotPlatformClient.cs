using System.Net.Http.Json;
using Core;
using Core.Lol.Identifiers;
using Ingestor.Options;
using Ingestor.Riot.Dto;
using Microsoft.Extensions.Options;

namespace Ingestor.Riot;

public sealed class RiotPlatformClient : IRiotPlatformClient
{
    private readonly HttpClient _httpClient;

    public RiotPlatformClient(HttpClient httpClient, IOptions<RiotOptions> options)
    {
        _httpClient = httpClient;
        var riotOptions = options.Value;

        if (string.IsNullOrWhiteSpace(riotOptions.ApiKey))
        {
            throw new InvalidOperationException("Missing Riot ApiKey. Configure Riot:ApiKey.");
        }

        if (!_httpClient.DefaultRequestHeaders.Contains("X-Riot-Token"))
        {
            _httpClient.DefaultRequestHeaders.Add("X-Riot-Token", riotOptions.ApiKey);
        }
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

    private async Task<T> GetAsync<T>(Uri uri, CancellationToken ct)
    {
        return await _httpClient.GetFromJsonAsync<T>(uri, ct)
            ?? throw new InvalidOperationException($"Empty response from Riot API ({uri}).");
    }

    private static Uri BuildPlatformUri(PlatformRoute platform, string path)
    {
        var host = RiotRouting.ToPlatformHost(platform);
        return new Uri($"https://{host}.api.riotgames.com{path}");
    }
}
