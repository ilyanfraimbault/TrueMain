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
        var resolved = ClientAddressResolver.Resolve(Context("172.16.1.12"), Header);

        Assert.Equal("172.16.1.12", resolved);
    }

    [Fact]
    public void UsesForwardedAddressRatherThanTheProxyConnection()
    {
        // The regression this whole change exists for: without the header the
        // key would be the web container's address for every visitor on the site.
        var resolved = ClientAddressResolver.Resolve(Context("172.16.1.12", "203.0.113.7"), Header);

        Assert.Equal("203.0.113.7", resolved);
    }

    [Fact]
    public void SeparatesTwoVisitorsArrivingThroughTheSameProxy()
    {
        var first = ClientAddressResolver.Resolve(Context("172.16.1.12", "203.0.113.7"), Header);
        var second = ClientAddressResolver.Resolve(Context("172.16.1.12", "198.51.100.4"), Header);

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

        Assert.Equal("203.0.113.7", ClientAddressResolver.Resolve(attacker, Header));
        Assert.Equal(
            ClientAddressResolver.Resolve(attacker, Header),
            ClientAddressResolver.Resolve(sameClientAgain, Header));
    }

    [Fact]
    public void TakesTheLastHopAcrossRepeatedHeaderLines()
    {
        var resolved = ClientAddressResolver.Resolve(
            Context("172.16.1.12", "1.2.3.4", "9.9.9.9, 203.0.113.7"),
            Header);

        Assert.Equal("203.0.113.7", resolved);
    }

    [Fact]
    public void IgnoresWhitespaceOnlyHeaderValues()
    {
        var resolved = ClientAddressResolver.Resolve(Context("172.16.1.12", "   "), Header);

        Assert.Equal("172.16.1.12", resolved);
    }

    [Fact]
    public void FallsBackToTheConnectionWhenTheHeaderLookupIsDisabled()
    {
        var resolved = ClientAddressResolver.Resolve(Context("172.16.1.12", "203.0.113.7"), headerName: "");

        Assert.Equal("172.16.1.12", resolved);
    }

    [Fact]
    public void ReturnsAStableKeyWhenNothingIdentifiesTheCaller()
    {
        var resolved = ClientAddressResolver.Resolve(Context(connectionAddress: null), Header);

        Assert.Equal("unknown", resolved);
    }
}
