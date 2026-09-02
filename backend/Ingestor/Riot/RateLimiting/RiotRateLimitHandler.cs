namespace Ingestor.Riot.RateLimiting;

/// <summary>
/// Holds every outbound Riot request until <see cref="IRiotRateLimiter"/> grants it a
/// permit for its routing value (#1359).
/// </summary>
/// <remarks>
/// Registered <strong>outside</strong> the resilience handler, and that placement is the
/// whole point. Inside it, the wait for a permit lands within the per-attempt timeout — 10
/// seconds — while a legitimate wait on a 100-per-2-minutes window is routinely longer than
/// that. Preprod showed exactly that failure within an hour of the first deploy: the attempt
/// timed out on the queue rather than on the network, retried into the same queue, and the
/// account's whole ingestion failed with a <c>TimeoutRejectedException</c> raised from the
/// limiter's own <c>Task.Delay</c>. Waiting outside means the wait is bounded by
/// <c>HttpClient.Timeout</c> instead, which is sized above the total request budget.
/// <para>
/// The consequence is that a retried attempt does not re-acquire a permit — the limiter sees
/// one logical request, not each physical attempt. That is why the feedback half lives in
/// <see cref="RiotRateLimitObserverHandler"/>, registered inside the resilience handler,
/// where it does see every attempt: the accounting is corrected from Riot's own
/// <c>X-App-Rate-Limit-Count</c> on the way back, which is what that header is for.
/// </para>
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

        return await base.SendAsync(request, cancellationToken);
    }
}

/// <summary>
/// Feeds every physical Riot response's rate-limit headers back into
/// <see cref="IRiotRateLimiter"/> (#1359).
/// </summary>
/// <remarks>
/// Separate from <see cref="RiotRateLimitHandler"/> because the two halves belong on
/// opposite sides of the resilience handler. Waiting must happen outside it, or a permit
/// wait is charged to the per-attempt timeout; observing must happen inside it, or the
/// limiter never sees the intermediate 429s the retry strategy absorbs — which are precisely
/// the responses carrying the <c>Retry-After</c> the next acquisition needs to honour.
/// Registered outside <see cref="RiotApiMetricsHandler"/> so the two stay independent.
/// </remarks>
internal sealed class RiotRateLimitObserverHandler(IRiotRateLimiter limiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        var (endpoint, routingValue) = RiotEndpointClassifier.Classify(request);
        if (routingValue is not null)
        {
            limiter.Observe(routingValue, endpoint, response);
        }

        return response;
    }
}
