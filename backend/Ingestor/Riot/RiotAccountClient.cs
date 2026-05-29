using System.Net.Http.Json;
using Core;
using Core.Lol.Identifiers;
using Ingestor.Options;
using Ingestor.Riot.Dto;
using Microsoft.Extensions.Options;

namespace Ingestor.Riot;

public sealed class RiotAccountClient : IRiotAccountClient
{
    private readonly HttpClient _httpClient;

    public RiotAccountClient(HttpClient httpClient, IOptions<RiotOptions> options)
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

    public async Task<RiotAccountDto> GetAccountByPuuidAsync(string puuid, RegionalRoute region, CancellationToken ct)
    {
        var uri = BuildRegionalUri(region, $"/riot/account/v1/accounts/by-puuid/{puuid}");
        return await _httpClient.GetFromJsonAsync<RiotAccountDto>(uri, ct)
            ?? throw new InvalidOperationException($"Empty response from Riot API ({uri}).");
    }

    private static Uri BuildRegionalUri(RegionalRoute region, string path)
    {
        var host = RiotRouting.ToRegionalHost(region);
        return new Uri($"https://{host}.api.riotgames.com{path}");
    }
}
