using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Ingestor.Riot;

public sealed class RiotHttpExecutor(ILogger<RiotHttpExecutor> logger) : IRiotHttpExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<T> GetAsync<T>(
        HttpClient httpClient,
        Uri uri,
        int maxRetryAttempts,
        string clientName,
        CancellationToken ct)
    {
        var attempts = Math.Max(1, maxRetryAttempts);

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await httpClient.SendAsync(request, ct);

            if (response.StatusCode == (HttpStatusCode)429)
            {
                if (attempt == attempts)
                {
                    break;
                }

                var delay = GetRetryDelay(response.Headers.RetryAfter, DateTimeOffset.UtcNow);
                logger.LogWarning(
                    "Riot API ({ClientName}) rate limited. Retrying in {Delay} (attempt {Attempt}/{MaxAttempts}).",
                    clientName,
                    delay,
                    attempt + 1,
                    attempts);

                await Task.Delay(delay, ct);
                continue;
            }

            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
                ?? throw new InvalidOperationException($"Empty response from Riot API ({uri}).");

            return payload;
        }

        throw new HttpRequestException($"Riot API request failed after {attempts} attempts.", null, HttpStatusCode.TooManyRequests);
    }

    internal static TimeSpan GetRetryDelay(RetryConditionHeaderValue? retryAfter, DateTimeOffset nowUtc)
    {
        if (retryAfter?.Delta is { } delta)
        {
            return delta <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var delay = date - nowUtc;
            return delay <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : delay;
        }

        return TimeSpan.FromSeconds(1);
    }
}
