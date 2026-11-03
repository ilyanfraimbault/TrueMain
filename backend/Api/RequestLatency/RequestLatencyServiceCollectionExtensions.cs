using TrueMain.Services.Ops.Diagnostics;

namespace TrueMain.RequestLatency;

public static class RequestLatencyServiceCollectionExtensions
{
    /// <summary>
    /// Registers the ops request-latency measurement (#2307): the in-memory accumulator the
    /// middleware records into, the flusher that writes it to Mongo, and the query behind
    /// <c>GET /ops/request-latency</c>. The store itself is registered by <c>AddMongoLogging</c>.
    /// </summary>
    public static IServiceCollection AddTrueMainRequestLatency(this IServiceCollection services)
    {
        services.AddSingleton<RequestLatencyAccumulator>();
        services.AddHostedService<RequestLatencyFlusher>();
        services.AddScoped<IRequestLatencyQueryService, RequestLatencyQueryService>();
        return services;
    }

    /// <summary>
    /// Adds <see cref="RequestLatencyMiddleware"/>. Call it before the exception handler, so
    /// the 500 that handler writes is the status recorded.
    /// </summary>
    public static IApplicationBuilder UseOpsRequestLatency(this IApplicationBuilder app)
        => app.UseMiddleware<RequestLatencyMiddleware>();
}
