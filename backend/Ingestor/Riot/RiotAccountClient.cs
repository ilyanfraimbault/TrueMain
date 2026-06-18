using System.Net;
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
        return await _httpClient.GetFromJsonStreamingAsync<RiotAccountDto>(uri, ct);
    }

    public async Task<RiotAccountDto?> GetByRiotIdAsync(string gameName, string tagLine, RegionalRoute regional, CancellationToken ct)
    {
        // gameName and tagLine are user-supplied and can carry spaces or other
        // characters that are unsafe in a path segment; encode each segment so
        // the Riot ID maps to a valid URL (e.g. a space becomes %20).
        var encodedName = Uri.EscapeDataString(gameName);
        var encodedTag = Uri.EscapeDataString(tagLine);
        var uri = BuildRegionalUri(regional, $"/riot/account/v1/accounts/by-riot-id/{encodedName}/{encodedTag}");

        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);

        // A 404 means Riot has no account for this Riot ID — a normal "not found"
        // outcome the seed flow surfaces to the caller, not a fault. Any other
        // non-success status is a transport/auth/rate-limit problem and must throw.
        // Drain the body before returning so the connection returns to the pool
        // rather than being abandoned (the response was read headers-only).
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            await response.Content.CopyToAsync(Stream.Null, ct);
            return null;
        }

        await response.EnsureSuccessDrainingAsync(ct);
        return await response.ReadFromJsonStreamingAsync<RiotAccountDto>(uri, ct);
    }

    private static Uri BuildRegionalUri(RegionalRoute region, string path)
    {
        var host = region.ToRegionalHost();
        return new Uri($"https://{host}.api.riotgames.com{path}");
    }
}
