using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Core;
using Ingestor.Options;
using Ingestor.Riot.Dto;
using Microsoft.Extensions.Options;

namespace Ingestor.Riot;

public class RiotAccountClient : IRiotAccountClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<RiotAccountClient> _logger;
    private readonly RiotOptions _options;

    public RiotAccountClient(HttpClient httpClient, IOptions<RiotOptions> options, ILogger<RiotAccountClient> logger)
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

    public Task<RiotAccountDto> GetAccountByPuuidAsync(string puuid, RegionalRoute region, CancellationToken ct)
    {
        var uri = BuildRegionalUri(region, $"/riot/account/v1/accounts/by-puuid/{puuid}");
        return GetAsync<RiotAccountDto>(uri, ct);
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
            return delay <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : delay;
        }

        return null;
    }

    private static Uri BuildRegionalUri(RegionalRoute region, string path)
    {
        var host = RiotRouting.ToRegionalHost(region);
        return new Uri($"https://{host}.api.riotgames.com{path}");
    }
}
