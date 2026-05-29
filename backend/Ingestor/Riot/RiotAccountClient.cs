using System.Net.Http.Json;
using Core;
using Core.Lol.Identifiers;
using Ingestor.Riot.Dto;

namespace Ingestor.Riot;

public sealed class RiotAccountClient : IRiotAccountClient
{
    private readonly HttpClient _httpClient;

    public RiotAccountClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<RiotAccountDto> GetAccountByPuuidAsync(string puuid, RegionalRoute region, CancellationToken ct)
    {
        var uri = BuildRegionalUri(region, $"/riot/account/v1/accounts/by-puuid/{puuid}");
        return await _httpClient.GetFromJsonAsync<RiotAccountDto>(uri, ct)
            ?? throw new InvalidOperationException($"Empty response from Riot API ({uri}).");
    }

    private static Uri BuildRegionalUri(RegionalRoute region, string path)
    {
        var host = region.ToRegionalHost();
        return new Uri($"https://{host}.api.riotgames.com{path}");
    }
}
