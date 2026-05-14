using Core;
using Core.Lol.Identifiers;
using Ingestor.Options;
using Ingestor.Riot.Dto;
using Microsoft.Extensions.Options;

namespace Ingestor.Riot;

public sealed class RiotAccountClient : IRiotAccountClient
{
    private readonly HttpClient _httpClient;
    private readonly RiotOptions _options;
    private readonly IRiotHttpExecutor _httpExecutor;

    public RiotAccountClient(HttpClient httpClient, IOptions<RiotOptions> options, IRiotHttpExecutor httpExecutor)
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

    public Task<RiotAccountDto> GetAccountByPuuidAsync(string puuid, RegionalRoute region, CancellationToken ct)
    {
        var uri = BuildRegionalUri(region, $"/riot/account/v1/accounts/by-puuid/{puuid}");
        return _httpExecutor.GetAsync<RiotAccountDto>(_httpClient, uri, _options.MaxRetryAttempts, nameof(RiotAccountClient), ct);
    }

    private static Uri BuildRegionalUri(RegionalRoute region, string path)
    {
        var host = RiotRouting.ToRegionalHost(region);
        return new Uri($"https://{host}.api.riotgames.com{path}");
    }
}
