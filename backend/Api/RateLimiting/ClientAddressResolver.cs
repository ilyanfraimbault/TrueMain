using System.Net;
using System.Net.Sockets;

namespace TrueMain.RateLimiting;

/// <summary>
/// Resolves the address a rate-limit partition should be keyed on.
/// </summary>
/// <remarks>
/// Lives in its own type rather than inline in the limiter because the rule it
/// encodes is a security boundary — see <see cref="Resolve"/> — and an inline
/// lambda in <c>Program.cs</c> cannot be tested.
/// </remarks>
public static class ClientAddressResolver
{
    /// <summary>
    /// The visitor's address: the <em>last</em> entry of
    /// <paramref name="headerName"/> when the request reached us through a
    /// trusted proxy, and the connection address otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two independent conditions guard the header, because a forwarded address
    /// is a claim and the limiter's whole value is that a caller cannot choose
    /// their own partition.
    /// </para>
    /// <para>
    /// First, the connection itself must come from a trusted proxy
    /// (<paramref name="trustedProxies"/>). The API container publishes port
    /// 8080 on the host, so "it is only reachable through Caddy" is a firewall
    /// property rather than an application one — and this codebase has already
    /// been bitten once by an upstream panel firewall whose behaviour was
    /// invisible from inside the box. A caller who reaches the port directly
    /// gets keyed on the address they actually connected from, whatever they
    /// claim in the header.
    /// </para>
    /// <para>
    /// Second, within the header the <em>last</em> entry wins. Our edge appends
    /// the peer it observed to whatever the caller already sent, so the value
    /// arrives as <c>&lt;caller-authored&gt;, &lt;observed by us&gt;</c>: every
    /// entry but the final one is attacker-controlled, and keying on an earlier
    /// one would hand a single client an unlimited supply of fresh partitions.
    /// </para>
    /// </remarks>
    public static string Resolve(
        HttpContext context,
        string headerName,
        IReadOnlyList<IPNetwork> trustedProxies)
    {
        var connection = context.Connection.RemoteIpAddress;

        if (!string.IsNullOrWhiteSpace(headerName)
            && IsTrustedProxy(connection, trustedProxies)
            && context.Request.Headers.TryGetValue(headerName, out var forwarded))
        {
            for (var line = forwarded.Count - 1; line >= 0; line--)
            {
                var hops = forwarded[line];
                if (string.IsNullOrWhiteSpace(hops))
                {
                    continue;
                }

                var lastHop = hops.AsSpan()[(hops.LastIndexOf(',') + 1)..].Trim();
                if (!lastHop.IsEmpty)
                {
                    return lastHop.ToString();
                }
            }
        }

        return connection?.ToString() ?? "unknown";
    }

    /// <summary>
    /// Parses CIDR notation (<c>172.16.0.0/12</c>) into the networks
    /// <see cref="Resolve"/> accepts a forwarded address from. Entries that do
    /// not parse are dropped rather than thrown on: startup validation rejects
    /// them up front, and a limiter that fails closed onto the connection
    /// address is the safe direction for a malformed value to fail in.
    /// </summary>
    public static IReadOnlyList<IPNetwork> ParseNetworks(IEnumerable<string> cidrs)
    {
        var networks = new List<IPNetwork>();
        foreach (var cidr in cidrs)
        {
            if (TryParseNetwork(cidr, out var network))
            {
                networks.Add(network);
            }
        }

        return networks;
    }

    /// <summary>
    /// Whether <paramref name="cidr"/> is well-formed CIDR notation. Used by
    /// startup validation so a typo in <c>RateLimit:TrustedProxies</c> fails the
    /// boot instead of silently disabling the forwarded-address lookup.
    /// </summary>
    public static bool IsParsableNetwork(string cidr) => TryParseNetwork(cidr, out _);

    private static bool TryParseNetwork(string cidr, out IPNetwork network)
    {
        network = default;
        var separator = cidr.LastIndexOf('/');
        if (separator < 0
            || !IPAddress.TryParse(cidr.AsSpan()[..separator], out var prefix)
            || !int.TryParse(cidr.AsSpan()[(separator + 1)..], out var prefixLength))
        {
            return false;
        }

        var maximumLength = prefix.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        if (prefixLength < 0 || prefixLength > maximumLength)
        {
            return false;
        }

        network = new IPNetwork(prefix, prefixLength);
        return true;
    }

    private static bool IsTrustedProxy(IPAddress? connection, IReadOnlyList<IPNetwork> trustedProxies)
    {
        if (connection is null)
        {
            return false;
        }

        // An IPv4 peer arriving on a dual-stack socket is presented as
        // ::ffff:a.b.c.d, which no IPv4 network contains. Unmap first so the
        // configured 172.16.0.0/12 still matches the container it names.
        var candidate = connection.IsIPv4MappedToIPv6 ? connection.MapToIPv4() : connection;

        for (var network = 0; network < trustedProxies.Count; network++)
        {
            if (trustedProxies[network].Contains(candidate))
            {
                return true;
            }
        }

        return false;
    }
}
