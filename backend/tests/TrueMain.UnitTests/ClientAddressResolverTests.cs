using System.Net;
using Microsoft.AspNetCore.Http;
using TrueMain.RateLimiting;

namespace TrueMain.UnitTests;

/// <summary>
/// The rate-limit partition key. Two properties matter and both are security
/// properties: real visitors behind the frontends' proxies must land in
/// different partitions (or one visitor's burst throttles the whole site), and
/// a caller must not be able to choose their own partition by sending a header.
/// </summary>
public class ClientAddressResolverTests
{
    private const string Header = "X-Forwarded-For";

    private static readonly IReadOnlyList<IPNetwork> TrustedProxies =
        ClientAddressResolver.ParseNetworks(["127.0.0.0/8", "::1/128", "172.16.0.0/12"]);

    private static HttpContext Context(string? connectionAddress, params string[] headerLines)
    {
        var context = new DefaultHttpContext();
        if (connectionAddress is not null)
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(connectionAddress);
        }

        if (headerLines.Length > 0)
        {
            context.Request.Headers[Header] = headerLines;
        }

        return context;
    }

    [Fact]
    public void UsesConnectionAddressWhenNoHeaderIsPresent()
    {
        var resolved = ClientAddressResolver.Resolve(Context("172.16.1.12"), Header, TrustedProxies);

        Assert.Equal("172.16.1.12", resolved);
    }

    [Fact]
    public void UsesForwardedAddressRatherThanTheProxyConnection()
    {
        // The regression this whole change exists for: without the header the
        // key would be the web container's address for every visitor on the site.
        var resolved = ClientAddressResolver.Resolve(Context("172.16.1.12", "203.0.113.7"), Header, TrustedProxies);

        Assert.Equal("203.0.113.7", resolved);
    }

    [Fact]
    public void SeparatesTwoVisitorsArrivingThroughTheSameProxy()
    {
        var first = ClientAddressResolver.Resolve(Context("172.16.1.12", "203.0.113.7"), Header, TrustedProxies);
        var second = ClientAddressResolver.Resolve(Context("172.16.1.12", "198.51.100.4"), Header, TrustedProxies);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void TakesTheLastHopSoASpoofedPrefixCannotMintPartitions()
    {
        // Our edge appends what it saw; anything before it was written by the
        // caller. Keying on the first entry would let one client send a fresh
        // value per request and never hit the limit.
        var attacker = Context("172.16.1.12", "1.2.3.4, 203.0.113.7");
        var sameClientAgain = Context("172.16.1.12", "9.9.9.9, 203.0.113.7");

        Assert.Equal("203.0.113.7", ClientAddressResolver.Resolve(attacker, Header, TrustedProxies));
        Assert.Equal(
            ClientAddressResolver.Resolve(attacker, Header, TrustedProxies),
            ClientAddressResolver.Resolve(sameClientAgain, Header, TrustedProxies));
    }

    [Fact]
    public void TakesTheLastHopAcrossRepeatedHeaderLines()
    {
        var resolved = ClientAddressResolver.Resolve(
            Context("172.16.1.12", "1.2.3.4", "9.9.9.9, 203.0.113.7"),
            Header,
            TrustedProxies);

        Assert.Equal("203.0.113.7", resolved);
    }

    [Fact]
    public void IgnoresWhitespaceOnlyHeaderValues()
    {
        var resolved = ClientAddressResolver.Resolve(Context("172.16.1.12", "   "), Header, TrustedProxies);

        Assert.Equal("172.16.1.12", resolved);
    }

    [Fact]
    public void FallsBackToTheConnectionWhenTheHeaderLookupIsDisabled()
    {
        var resolved = ClientAddressResolver.Resolve(Context("172.16.1.12", "203.0.113.7"), headerName: "", TrustedProxies);

        Assert.Equal("172.16.1.12", resolved);
    }

    [Fact]
    public void ReturnsAStableKeyWhenNothingIdentifiesTheCaller()
    {
        var resolved = ClientAddressResolver.Resolve(Context(connectionAddress: null), Header, TrustedProxies);

        Assert.Equal("unknown", resolved);
    }

    [Fact]
    public void IgnoresTheHeaderFromAnUntrustedConnection()
    {
        // The API container publishes its port on the host, so reaching it
        // without passing through the edge is a firewall question, not an
        // application one. A caller who does gets keyed on where they actually
        // came from, whatever they claim to be forwarding.
        var direct = Context("203.0.113.99", "10.0.0.1");

        Assert.Equal("203.0.113.99", ClientAddressResolver.Resolve(direct, Header, TrustedProxies));
    }

    [Fact]
    public void ForgedHeadersFromAnUntrustedConnectionCannotMintPartitions()
    {
        var first = Context("203.0.113.99", "10.0.0.1");
        var second = Context("203.0.113.99", "10.0.0.2");

        Assert.Equal(
            ClientAddressResolver.Resolve(first, Header, TrustedProxies),
            ClientAddressResolver.Resolve(second, Header, TrustedProxies));
    }

    [Fact]
    public void TrustsAProxyPresentedAsAnIPv4MappedIPv6Peer()
    {
        // Dual-stack sockets present 172.16.1.12 as ::ffff:172.16.1.12, which
        // no IPv4 range contains until it is unmapped.
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:172.16.1.12");
        context.Request.Headers[Header] = "203.0.113.7";

        Assert.Equal("203.0.113.7", ClientAddressResolver.Resolve(context, Header, TrustedProxies));
    }

    [Fact]
    public void RejectsMalformedTrustedProxyRanges()
    {
        Assert.False(ClientAddressResolver.IsParsableNetwork("172.16.0.0"));
        Assert.False(ClientAddressResolver.IsParsableNetwork("172.16.0.0/33"));
        Assert.True(ClientAddressResolver.IsParsableNetwork("172.16.0.0/12"));
        Assert.True(ClientAddressResolver.IsParsableNetwork("::1/128"));
    }
}
