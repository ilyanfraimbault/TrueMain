using System.Net;
using Microsoft.Extensions.Options;
using TrueMain.Options;

namespace TrueMain.RateLimiting;

/// <summary>
/// The parsed <c>RateLimit:TrustedProxies</c> ranges, resolved once.
/// </summary>
/// <remarks>
/// Registered as a singleton because the alternative is parsing the same
/// handful of CIDR strings inside the partition factory, which runs on every
/// request the API serves. The values come from the environment and do not
/// change without a restart.
/// </remarks>
public sealed class TrustedProxyNetworks
{
    public TrustedProxyNetworks(IOptions<RateLimitOptions> options)
        => Networks = ClientAddressResolver.ParseNetworks(options.Value.TrustedProxies);

    /// <summary>Networks allowed to speak for another address.</summary>
    public IReadOnlyList<IPNetwork> Networks { get; }
}
