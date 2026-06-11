using System.Net;
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

    public async Task<RiotAccountDto?> GetByRiotIdAsync(string gameName, string tagLine, RegionalRoute regional, CancellationToken ct)
    {
        // gameName and tagLine are user-supplied and can carry spaces or other
        // characters that are unsafe in a path segment; encode each segment so
        // the Riot ID maps to a valid URL (e.g. a space becomes %20).
        var encodedName = Uri.EscapeDataString(gameName);
        var encodedTag = Uri.EscapeDataString(tagLine);
        var uri = BuildRegionalUri(regional, $"/riot/account/v1/accounts/by-riot-id/{encodedName}/{encodedTag}");

        using var response = await _httpClient.GetAsync(uri, ct);

        // A 404 means Riot has no account for this Riot ID — a normal "not found"
        // outcome the seed flow surfaces to the caller, not a fault. Any other
        // non-success status is a transport/auth/rate-limit problem and must throw.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RiotAccountDto>(ct)
            ?? throw new InvalidOperationException($"Empty response from Riot API ({uri}).");
    }

    private static Uri BuildRegionalUri(RegionalRoute region, string path)
    {
        var host = region.ToRegionalHost();
        return new Uri($"https://{host}.api.riotgames.com{path}");
    }
}
