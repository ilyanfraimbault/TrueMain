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

    private static readonly TimeSpan DefaultBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

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
            HttpResponseMessage? response = null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                response = await httpClient.SendAsync(request, ct);

                if (response.StatusCode == (HttpStatusCode)429)
                {
                    if (attempt == attempts)
                    {
                        break;
                    }

                    var delay = GetRetryDelay(response.Headers.RetryAfter, DateTimeOffset.UtcNow);
                    LogRetry(clientName, "rate limited", delay, attempt, attempts);
                    await Task.Delay(delay, ct);
                    continue;
                }

                if (IsTransientServerError(response.StatusCode))
                {
                    if (attempt == attempts)
                    {
                        response.EnsureSuccessStatusCode();
                    }

                    var delay = GetExponentialBackoff(attempt);
                    LogRetry(clientName, $"transient {(int)response.StatusCode}", delay, attempt, attempts);
                    await Task.Delay(delay, ct);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var payload = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
                    ?? throw new InvalidOperationException($"Empty response from Riot API ({uri}).");

                return payload;
            }
            catch (HttpRequestException ex) when (IsTransientNetworkException(ex) && attempt < attempts)
            {
                var delay = GetExponentialBackoff(attempt);
                LogRetry(clientName, $"network failure: {ex.Message}", delay, attempt, attempts);
                await Task.Delay(delay, ct);
            }
            finally
            {
                response?.Dispose();
            }
        }

        throw new HttpRequestException(
            $"Riot API request failed after {attempts} attempts.",
            null,
            HttpStatusCode.TooManyRequests);
    }

    internal static TimeSpan GetRetryDelay(RetryConditionHeaderValue? retryAfter, DateTimeOffset nowUtc)
    {
        if (retryAfter?.Delta is { } delta)
        {
            return delta <= TimeSpan.Zero ? DefaultBackoff : delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var delay = date - nowUtc;
            return delay <= TimeSpan.Zero ? DefaultBackoff : delay;
        }

        return DefaultBackoff;
    }

    internal static TimeSpan GetExponentialBackoff(int attempt)
    {
        // 1s, 2s, 4s, 8s, 16s, capped at 30s. Attempt is 1-based.
        var seconds = Math.Pow(2, Math.Max(0, attempt - 1));
        var backoff = TimeSpan.FromSeconds(seconds);
        return backoff > MaxBackoff ? MaxBackoff : backoff;
    }

    internal static bool IsTransientServerError(HttpStatusCode statusCode)
    {
        // Retry only on the server-side faults Riot themselves recommend retrying.
        // 4xx other than 429 indicate caller bugs and are propagated immediately.
        return statusCode is HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }

    private static bool IsTransientNetworkException(HttpRequestException ex)
    {
        // HttpRequestException with no status code wraps socket-level / DNS / TLS issues.
        // Anything coming back with a status was already classified above.
        return ex.StatusCode is null;
    }

    private void LogRetry(string clientName, string reason, TimeSpan delay, int attempt, int maxAttempts)
    {
        logger.LogWarning(
            "Riot API ({ClientName}) {Reason}. Retrying in {Delay} (attempt {Attempt}/{MaxAttempts}).",
            clientName,
            reason,
            delay,
            attempt + 1,
            maxAttempts);
    }
}
