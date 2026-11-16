using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using TrueMain.Configuration;
using TrueMain.Options;
using TrueMain.Services.Champions.Warmup;

namespace TrueMain.RateLimiting;

/// <summary>
/// Registers the public API's throttling.
/// </summary>
public static class RateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddTrueMainRateLimiting(this IServiceCollection services)
    {
        // Rate limiting: one global per-visitor fixed window shields the public
        // champion endpoints from casual abuse. There is no separate ops policy — the
        // ops endpoints share this window.
        //
        // "Per visitor", not per connection: both frontends proxy every call through
        // their own server, so the connection address is one of two containers and
        // keying on it would throttle the whole site as if it were a single client.
        // RateLimitOptions carries the reasoning and the reason the header is read
        // back-to-front.
        services.AddApiOptions<RateLimitOptions>();
        services.AddSingleton<TrustedProxyNetworks>();
        services.AddSingleton<RateLimitRejectionRecorder>();
        services.AddHostedService<RateLimitRejectionSummaryService>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            // The middleware only reports a rejection at Debug, which never reaches
            // the ops logs; the recorder turns it into rows an operator can count
            // without letting a flood of 429s fill the log channel (#1555).
            options.OnRejected = (rejection, _) =>
            {
                var context = rejection.HttpContext;
                if (rejection.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.Response.Headers.RetryAfter =
                        Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                var limits = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
                var trustedProxies = context.RequestServices.GetRequiredService<TrustedProxyNetworks>();
                context.RequestServices.GetRequiredService<RateLimitRejectionRecorder>().Record(
                    ClientAddressResolver.Resolve(context, limits.ClientIpHeader, trustedProxies.Networks),
                    context.Request.Method,
                    context.Request.Path.Value ?? "/");
                return ValueTask.CompletedTask;
            };
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                // The API warming its own cache (#2425): thousands of loopback requests, each
                // carrying a key drawn for this process. Not a visitor to limit.
                if (context.RequestServices.GetRequiredService<ChampionReadVersion>().IsWarming)
                {
                    return RateLimitPartition.GetNoLimiter("champion-warmup");
                }

                var limits = context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
                var trustedProxies = context.RequestServices.GetRequiredService<TrustedProxyNetworks>();
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ClientAddressResolver.Resolve(context, limits.ClientIpHeader, trustedProxies.Networks),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.PermitLimit,
                        Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                        QueueLimit = limits.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });
        });

        return services;
    }
}
