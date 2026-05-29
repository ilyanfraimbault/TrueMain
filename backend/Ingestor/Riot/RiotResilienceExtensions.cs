using Ingestor.Options;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Ingestor.Riot;

/// <summary>
/// Registers the standard HTTP resilience handler for the Riot API typed clients.
/// </summary>
public static class RiotResilienceExtensions
{
    /// <summary>
    /// Adds the standard resilience handler (rate limiter, total timeout, retry with
    /// exponential backoff and jitter, circuit breaker, per-attempt timeout) to a Riot
    /// API typed client and tunes the retry count from <see cref="RiotOptions"/>.
    /// </summary>
    /// <param name="builder">The HTTP client builder to configure.</param>
    /// <returns>The same <paramref name="builder"/> so that calls can be chained.</returns>
    public static IHttpClientBuilder AddRiotResilienceHandler(this IHttpClientBuilder builder)
    {
        builder.AddStandardResilienceHandler().Configure((options, serviceProvider) =>
        {
            var riotOptions = serviceProvider.GetRequiredService<IOptionsMonitor<RiotOptions>>().CurrentValue;

            options.Retry.MaxRetryAttempts = riotOptions.MaxRetryAttempts;

            // The standard handler validates that the total request timeout is large enough
            // to cover every attempt at the per-attempt timeout. Keep them consistent when the
            // configured retry count exceeds what the 30s default allows.
            var minimumTotalTimeout = options.AttemptTimeout.Timeout * (riotOptions.MaxRetryAttempts + 1);
            if (options.TotalRequestTimeout.Timeout < minimumTotalTimeout)
            {
                options.TotalRequestTimeout.Timeout = minimumTotalTimeout;
            }
        });

        return builder;
    }
}
