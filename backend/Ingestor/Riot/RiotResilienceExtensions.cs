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
    /// API typed client and tunes the retry count and timeouts from <see cref="RiotOptions"/>.
    /// </summary>
    /// <remarks>
    /// The retry strategy honours <c>Retry-After</c> headers by default
    /// (<c>HttpRetryStrategyOptions.ShouldRetryAfterHeader</c>), and those waits count
    /// against the total request timeout. Riot's app-rate-limit windows can demand
    /// waits beyond 100 seconds, so the total timeout default is sized to survive at
    /// least one such backoff followed by a successful attempt (see issue #443, where
    /// a 40s total timeout made every Discovery run fail mid-backoff).
    /// </remarks>
    /// <param name="builder">The HTTP client builder to configure.</param>
    /// <returns>The same <paramref name="builder"/> so that calls can be chained.</returns>
    public static IHttpClientBuilder AddRiotResilienceHandler(this IHttpClientBuilder builder)
    {
        builder.AddStandardResilienceHandler().Configure((options, serviceProvider) =>
        {
            // IOptions, not IOptionsMonitor: this Configure delegate runs once, when the handler's
            // options are built, so a monitor would promise a hot reload that never reaches the
            // pipeline. Changing Riot:* takes a restart.
            var riotOptions = serviceProvider.GetRequiredService<IOptions<RiotOptions>>().Value;

            options.Retry.MaxRetryAttempts = riotOptions.MaxRetryAttempts;
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(riotOptions.AttemptTimeoutSeconds);

            // Invariant: the total request timeout must always be large enough to cover
            // every attempt at the per-attempt timeout, whatever values are configured.
            // Derived from the shared calculation so it can never drift from the value
            // ConfigureRiotClient sizes HttpClient.Timeout around (#855).
            options.TotalRequestTimeout.Timeout = riotOptions.EffectiveTotalRequestTimeout();

            // The standard handler validates that the circuit breaker's sampling duration
            // is at least double the attempt timeout; keep it valid when the configured
            // attempt timeout exceeds what the 30s sampling default allows.
            var minimumSamplingDuration = options.AttemptTimeout.Timeout * 2;
            if (options.CircuitBreaker.SamplingDuration < minimumSamplingDuration)
            {
                options.CircuitBreaker.SamplingDuration = minimumSamplingDuration;
            }
        });

        return builder;
    }
}
