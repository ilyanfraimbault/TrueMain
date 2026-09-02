namespace Ingestor.Riot.RateLimiting;

/// <summary>
/// Holds every outbound Riot request until <see cref="IRiotRateLimiter"/> grants it a
/// permit for its routing value, then feeds the response's rate-limit headers back
/// into the limiter (#1359).
/// </summary>
/// <remarks>
/// Registered <em>inside</em> the resilience handler, so a retried attempt waits for
/// its own permit rather than replaying straight into the limit that rejected it, and
/// <em>outside</em> <see cref="RiotApiMetricsHandler"/>, so the time spent waiting for
/// a permit is not recorded as Riot latency.
/// </remarks>
internal sealed class RiotRateLimitHandler(IRiotRateLimiter limiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var (endpoint, routingValue) = RiotEndpointClassifier.Classify(request);
        if (routingValue is null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        await limiter.AcquireAsync(routingValue, endpoint, cancellationToken);

        var response = await base.SendAsync(request, cancellationToken);
        limiter.Observe(routingValue, endpoint, response);
        return response;
    }
}
