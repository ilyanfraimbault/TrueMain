using Ingestor.Options;
using Ingestor.Riot.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Ingestor.Riot;

/// <summary>
/// Registers a typed Riot API client on the pipeline every Riot client shares, so the handler
/// order, the client timeout and the one rate limiter are decided here and nowhere else.
/// </summary>
public static class RiotClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TImplementation"/> as <typeparamref name="TClient"/> behind
    /// the shared Riot pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Handler order, outermost first: rate limiter → resilience → rate-limit observer → metrics.
    /// The two halves of the limiter sit on opposite sides of the resilience handler, and both
    /// placements are load-bearing. Waiting goes OUTSIDE it: inside, a permit wait is charged to
    /// the 10-second per-attempt timeout, while a legitimate wait on a 100-per-2-minutes window
    /// is routinely longer than that — preprod failed whole accounts that way within an hour of
    /// the first deploy. Observing goes INSIDE it, where it sees every physical attempt including
    /// the 429s the retry strategy absorbs, which carry the Retry-After the next acquisition must
    /// honour. The metrics handler stays innermost so a permit wait is never recorded as Riot
    /// latency, and it records every physical attempt, retried 429s included (#93).
    /// </para>
    /// <para>
    /// The retry vs permit rule that follows: a retried attempt does not take a second permit —
    /// the resilience handler waits out the 429's Retry-After itself, and the observer charges
    /// that same penalty to the next acquisition on the routing value (#1359).
    /// </para>
    /// <para>
    /// One limiter for the whole process, shared by every client: Riot's application budget
    /// belongs to the routing value, not to the client that happens to call it, so the match
    /// client and the account client hitting <c>europe</c> draw on the same budget (#1359).
    /// Every client also asks for compressed responses (#1601).
    /// </para>
    /// </remarks>
    public static IHttpClientBuilder AddRiotClient<TClient, TImplementation>(this IServiceCollection services)
        where TClient : class
        where TImplementation : class, TClient
    {
        services.TryAddSingleton<IRiotRateLimiter, RiotRateLimiter>();
        services.TryAddTransient<RiotRateLimitHandler>();
        services.TryAddTransient<RiotRateLimitObserverHandler>();
        services.TryAddTransient<RiotApiMetricsHandler>();

        return services.AddHttpClient<TClient, TImplementation>(ConfigureClient)
            .AcceptCompressedResponses()
            .AddHttpMessageHandler<RiotRateLimitHandler>()
            .AddRiotResilienceHandler()
            .AddHttpMessageHandler<RiotRateLimitObserverHandler>()
            .AddHttpMessageHandler<RiotApiMetricsHandler>();
    }

    /// <summary>
    /// <c>HttpClient.Timeout</c> for a Riot client. It wraps the whole pipeline — the permit wait
    /// outside the resilience handler included — and its 100 s default would silently cut a
    /// longer one short with an opaque <see cref="TaskCanceledException"/> (#855). So it is the
    /// resilience handler's own total, plus the longest permit wait
    /// (<c>RiotRateLimit:MaxPermitWaitSeconds</c>), plus a margin.
    /// </summary>
    public static TimeSpan ClientTimeout(RiotOptions riot, RiotRateLimitOptions rateLimit)
        => riot.EffectiveTotalRequestTimeout()
           + TimeSpan.FromSeconds(rateLimit.MaxPermitWaitSeconds)
           + TimeSpan.FromSeconds(5);

    private static void ConfigureClient(IServiceProvider serviceProvider, HttpClient client)
    {
        var riot = serviceProvider.GetRequiredService<IOptions<RiotOptions>>().Value;
        var rateLimit = serviceProvider.GetRequiredService<IOptions<RiotRateLimitOptions>>().Value;
        client.DefaultRequestHeaders.Add("X-Riot-Token", riot.ApiKey);
        client.Timeout = ClientTimeout(riot, rateLimit);
    }
}
