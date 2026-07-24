using Ingestor.Options;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

namespace Ingestor.BuildFacts;

/// <summary>
/// Registers the standard HTTP resilience handler for the CommunityDragon typed client.
/// </summary>
public static class CommunityDragonResilienceExtensions
{
    /// <summary>
    /// Adds the standard resilience handler (rate limiter, total timeout, retry with
    /// exponential backoff and jitter, circuit breaker, per-attempt timeout) to the
    /// CommunityDragon typed client and tunes it from <see cref="CommunityDragonOptions"/>.
    /// </summary>
    /// <remarks>
    /// This mirrors <c>Ingestor.Riot.RiotResilienceExtensions</c> with one deliberate
    /// difference: CommunityDragon serves static files and never demands a multi-minute
    /// <c>Retry-After</c> wait, so the total request timeout is a hard ceiling here. When
    /// the attempts cannot fit inside it, the per-attempt timeout is clamped *down* rather
    /// than the total being stretched up — the metadata fetch must stay inside the budget
    /// its caller allows (see <c>Ingestor/Program.cs</c>, which sizes
    /// <see cref="HttpClient.Timeout"/> from the same options).
    /// The pipeline's own telemetry is emitted under the <c>Polly</c> log category, which
    /// the Mongo sink already filters to Error in appsettings, so retries do not
    /// reintroduce the ops-log noise removed in issue #444.
    /// </remarks>
    /// <param name="builder">The HTTP client builder to configure.</param>
    /// <returns>The same <paramref name="builder"/> so that calls can be chained.</returns>
    public static IHttpClientBuilder AddCommunityDragonResilienceHandler(this IHttpClientBuilder builder)
    {
        builder.AddStandardResilienceHandler().Configure((options, serviceProvider) =>
        {
            var communityDragonOptions = serviceProvider
                .GetRequiredService<IOptionsMonitor<CommunityDragonOptions>>().CurrentValue;

            options.Retry.MaxRetryAttempts = communityDragonOptions.MaxRetryAttempts;
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(communityDragonOptions.AttemptTimeoutSeconds);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(communityDragonOptions.TotalRequestTimeoutSeconds);

            // Invariant: every attempt must fit inside the total budget, whatever values are
            // configured. Shrinking the attempt timeout keeps the total — and therefore the
            // caller's HttpClient.Timeout — authoritative.
            var attemptBudget = options.TotalRequestTimeout.Timeout / (communityDragonOptions.MaxRetryAttempts + 1);
            if (options.AttemptTimeout.Timeout > attemptBudget)
            {
                options.AttemptTimeout.Timeout = attemptBudget;
            }

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
