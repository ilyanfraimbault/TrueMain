using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using TrueMain.Options;

namespace TrueMain.RateLimiting;

/// <summary>
/// Registers the public API's throttling.
/// </summary>
/// <remarks>
/// Extracted from <c>Program.cs</c> rather than left inline: the file is over
/// the size guardrail's limit and may only shrink, and the partition rule is
/// involved enough to deserve reading on its own.
/// </remarks>
public static class RateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddTrueMainRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
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
        services.AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .Validate(options => options.PermitLimit > 0, "RateLimit:PermitLimit must be greater than 0.")
            .Validate(options => options.WindowSeconds > 0, "RateLimit:WindowSeconds must be greater than 0.")
            .Validate(options => options.QueueLimit >= 0, "RateLimit:QueueLimit must be >= 0.")
            .Validate(
                options => options.TrustedProxies.All(ClientAddressResolver.IsParsableNetwork),
                "RateLimit:TrustedProxies must be CIDR ranges, e.g. \"172.16.0.0/12\". A typo here would "
                + "silently stop the limiter from reading the forwarded address and collapse every visitor "
                + "back onto the proxy's own partition.")
            .ValidateOnStart();
        services.AddSingleton<TrustedProxyNetworks>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
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
