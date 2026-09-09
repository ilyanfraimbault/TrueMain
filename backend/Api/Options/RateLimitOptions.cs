namespace TrueMain.Options;

/// <summary>
/// Public-API throttling, bound from <c>RateLimit:*</c>. Every value is
/// configuration rather than a constant because the failure mode this section
/// exists to prevent is a traffic spike, and a spike is precisely when a code
/// change is the slowest lever available: prod only rolls on a published
/// release, so a compiled-in limit cannot be relaxed while the site is being
/// hit.
/// </summary>
/// <remarks>
/// <para>
/// The partition key is the <em>visitor's</em> IP, which is not the IP the API
/// sees. No browser talks to this API: the public site proxies every read
/// through its Nuxt server (<c>web/server/api/[...path].ts</c>) and the admin
/// portal does the same for <c>/ops</c>, so every request arrives from one of
/// two container addresses. Partitioning on the connection address therefore
/// collapses the entire internet into a single bucket, and the limit stops
/// being "per visitor" and becomes a site-wide ceiling — which is how a burst
/// of real traffic served itself 429s rather than pages.
/// </para>
/// <para>
/// <see cref="ClientIpHeader"/> is read <em>last entry first</em>, and that
/// direction is the whole security of it. Caddy <em>appends</em> the peer it
/// observed to any <c>X-Forwarded-For</c> the client already sent, so the
/// header reaching this API reads
/// <c>&lt;anything the client made up&gt;, &lt;the address Caddy saw&gt;</c>.
/// The last entry is the only one written by our own edge; the earlier ones are
/// attacker-controlled, and keying on them would hand any caller an unlimited
/// supply of fresh buckets.
/// </para>
/// </remarks>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    /// <summary>
    /// Requests allowed per <see cref="WindowSeconds"/>, per visitor. Sized for
    /// a real reader rather than for the whole site: a champion page fans out
    /// into a handful of reads, so a person browsing quickly stays an order of
    /// magnitude under this while a scraper does not.
    /// </summary>
    public int PermitLimit { get; set; } = 500;

    /// <summary>Length of the fixed window, in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>
    /// Requests held rather than rejected once a visitor's window is spent.
    /// Kept small on purpose: a queued request occupies a connection while it
    /// waits, so a deep queue converts a burst of clean 429s into 502s and 504s
    /// further up the chain — which is what the edge logged during the spike
    /// this section was written for.
    /// </summary>
    public int QueueLimit { get; set; } = 10;

    /// <summary>
    /// Networks, in CIDR notation, whose connections may speak for another
    /// address through <see cref="ClientIpHeader"/>. Defaults to the private
    /// ranges, which is where both frontend proxies sit; anything reaching the
    /// published container port from elsewhere is keyed on the address it
    /// actually connected from, so the header cannot be used to escape the
    /// limit. Empty disables the lookup entirely.
    /// </summary>
    public string[] TrustedProxies { get; set; } =
        ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7"];

    /// <summary>
    /// Header carrying the forwarded client address. Empty disables the lookup
    /// and falls back to the connection address, which is the correct behaviour
    /// for a deployment where nothing sits in front of the API — and the wrong
    /// one for ours.
    /// </summary>
    public string ClientIpHeader { get; set; } = "X-Forwarded-For";
}
