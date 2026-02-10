using Core;
using Ingestor.Options;
using Ingestor.Riot.Dto;
using Microsoft.Extensions.Options;

namespace Ingestor.Riot;

public sealed class RiotPlatformClient : IRiotPlatformClient
{
    private readonly HttpClient _httpClient;
    private readonly RiotOptions _options;
    private readonly IRiotHttpExecutor _httpExecutor;

    public RiotPlatformClient(HttpClient httpClient, IOptions<RiotOptions> options, IRiotHttpExecutor httpExecutor)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpExecutor = httpExecutor;

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Missing Riot ApiKey. Configure Riot:ApiKey.");
        }

        if (!_httpClient.DefaultRequestHeaders.Contains("X-Riot-Token"))
        {
            _httpClient.DefaultRequestHeaders.Add("X-Riot-Token", _options.ApiKey);
        }
    }

    public Task<RiotLeagueListDto> GetChallengerLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
    {
        var uri = BuildPlatformUri(platform, $"/lol/league/v4/challengerleagues/by-queue/{queue}");
        return _httpExecutor.GetAsync<RiotLeagueListDto>(_httpClient, uri, _options.MaxRetryAttempts, nameof(RiotPlatformClient), ct);
    }

    public Task<RiotLeagueListDto> GetGrandmasterLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
    {
        var uri = BuildPlatformUri(platform, $"/lol/league/v4/grandmasterleagues/by-queue/{queue}");
        return _httpExecutor.GetAsync<RiotLeagueListDto>(_httpClient, uri, _options.MaxRetryAttempts, nameof(RiotPlatformClient), ct);
    }

    public Task<RiotLeagueListDto> GetMasterLeagueAsync(PlatformRoute platform, string queue, CancellationToken ct)
    {
        var uri = BuildPlatformUri(platform, $"/lol/league/v4/masterleagues/by-queue/{queue}");
        return _httpExecutor.GetAsync<RiotLeagueListDto>(_httpClient, uri, _options.MaxRetryAttempts, nameof(RiotPlatformClient), ct);
    }

    public Task<RiotSummonerDto> GetSummonerAsync(PlatformRoute platform, string summonerId, CancellationToken ct)
    {
        var uri = BuildPlatformUri(platform, $"/lol/summoner/v4/summoners/{summonerId}");
        return _httpExecutor.GetAsync<RiotSummonerDto>(_httpClient, uri, _options.MaxRetryAttempts, nameof(RiotPlatformClient), ct);
    }

    public Task<RiotSummonerDto> GetSummonerByPuuidAsync(PlatformRoute platform, string puuid, CancellationToken ct)
    {
        var uri = BuildPlatformUri(platform, $"/lol/summoner/v4/summoners/by-puuid/{puuid}");
        return _httpExecutor.GetAsync<RiotSummonerDto>(_httpClient, uri, _options.MaxRetryAttempts, nameof(RiotPlatformClient), ct);
    }

    public Task<List<RiotChampionMasteryDto>> GetChampionMasteriesAsync(PlatformRoute platform, string puuid, CancellationToken ct)
    {
        var uri = BuildPlatformUri(platform, $"/lol/champion-mastery/v4/champion-masteries/by-puuid/{puuid}");
        return _httpExecutor.GetAsync<List<RiotChampionMasteryDto>>(_httpClient, uri, _options.MaxRetryAttempts, nameof(RiotPlatformClient), ct);
    }

    private static Uri BuildPlatformUri(PlatformRoute platform, string path)
    {
        var host = RiotRouting.ToPlatformHost(platform);
        return new Uri($"https://{host}.api.riotgames.com{path}");
    }
}
