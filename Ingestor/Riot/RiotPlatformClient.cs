using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Core;
using Ingestor.Options;
using Ingestor.Riot.Dto;
using Microsoft.Extensions.Options;

namespace Ingestor.Riot;

public class RiotPlatformClient : IRiotPlatformClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<RiotPlatformClient> _logger;
    private readonly RiotOptions _options;

    public RiotPlatformClient(HttpClient httpClient, IOptions<RiotOptions> options, ILogger<RiotPlatformClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;

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

    private async Task<T> GetAsync<T>(Uri uri, CancellationToken ct)
    {
        var maxAttempts = Math.Max(1, _options.MaxRetryAttempts);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _httpClient.SendAsync(request, ct);

            if (response.StatusCode == (HttpStatusCode)429)
            {
                if (attempt == maxAttempts)
                {
                    break;
                }

                var delay = GetRetryDelay(response);
                if (delay is null)
                {
                    break;
                }

                _logger.LogWarning(
                    "Riot API rate limited. Retrying in {Delay} (attempt {Attempt}/{MaxAttempts}).",
                    delay.Value,
                    attempt + 1,
                    maxAttempts);

                await Task.Delay(delay.Value, ct);
                continue;
            }

            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
            if (payload is null)
            {
                throw new InvalidOperationException($"Empty response from Riot API ({uri}).");
            }

            return payload;
        }

        throw new HttpRequestException($"Riot API request failed after {maxAttempts} attempts.", null, HttpStatusCode.TooManyRequests);
    }

    private static TimeSpan? GetRetryDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta.HasValue)
        {
            return retryAfter.Delta.Value;
        }

        if (retryAfter.Date.HasValue)
        {
            var delay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
            return delay <= TimeSpan.Zero ? TimeSpan.Zero : delay;
        }

        return null;
    }

    private static Uri BuildPlatformUri(PlatformRoute platform, string path)
    {
        var host = RiotRouting.ToPlatformHost(platform);
        return new Uri($"https://{host}.api.riotgames.com{path}");
    }
}
