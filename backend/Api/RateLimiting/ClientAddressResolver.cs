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
    /// <paramref name="headerName"/>, falling back to the connection address.
    /// </summary>
    /// <remarks>
    /// Reading the header back-to-front is the point. Our edge appends the peer
    /// it observed to whatever the caller already sent, so the header arrives as
    /// <c>&lt;caller-authored&gt;, &lt;observed by us&gt;</c>: only the final
    /// entry is trustworthy, and keying on an earlier one would let a single
    /// caller mint a fresh partition per request and walk past the limit
    /// entirely. Multiple header lines are scanned from the last backwards for
    /// the same reason.
    /// </remarks>
    public static string Resolve(HttpContext context, string headerName)
    {
        if (!string.IsNullOrWhiteSpace(headerName)
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

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
