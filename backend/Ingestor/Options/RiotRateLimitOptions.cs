namespace Ingestor.Options;

/// <summary>
/// Pacing of outbound Riot API calls (#1359). Riot enforces its application limit
/// <em>per routing value</em> — <c>europe</c>, <c>americas</c> and <c>asia</c> for the
/// regional APIs, <c>euw1</c>, <c>kr</c> and <c>na1</c> for the platform ones — so the
/// limiter keeps one budget per routing value rather than one for the whole process.
/// </summary>
public class RiotRateLimitOptions
{
    public const string SectionName = "RiotRateLimit";

    /// <summary>
    /// Whether outbound calls wait for a permit. Disabling falls back to the previous
    /// behaviour: no pacing at all, 429s discovered by taking them.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Application limits assumed before Riot has advertised any, in Riot's own header
    /// format ("requests:seconds,requests:seconds"). The default is the personal-key
    /// allowance; a production key advertises its own numbers on the first response and
    /// the limiter adopts them without a deploy.
    /// </summary>
    public string AppLimits { get; set; } = "20:1,100:120";

    /// <summary>
    /// Whether the per-endpoint method limits Riot advertises are enforced alongside the
    /// application limit. There is no configured default for them: a method budget is
    /// only ever learned from a response header.
    /// </summary>
    public bool EnforceMethodLimits { get; set; } = true;

    /// <summary>
    /// Fraction of every window held back (0.05 = use 95% of the advertised limit).
    /// Riot counts requests we cannot see — a retry that never reached this handler, a
    /// second process sharing the key — and its windows are fixed where ours are sliding,
    /// so spending the last permit of a window is what turns a small mismatch into a 429.
    /// </summary>
    public double SafetyHeadroom { get; set; } = 0.05;

    /// <summary>
    /// Penalty applied to a bucket when Riot answers 429 without a <c>Retry-After</c>
    /// header, in seconds.
    /// </summary>
    public int DefaultRetryAfterSeconds { get; set; } = 5;

    /// <summary>
    /// Longest a single call may wait for a permit, in seconds. Serves two purposes: it is
    /// added to the Riot clients' <c>HttpClient.Timeout</c>, which now has to cover the wait
    /// <em>and</em> the resilience pipeline beneath it, and it caps a single acquisition.
    /// </summary>
    /// <remarks>
    /// The default covers a ten-minute window, which is the longest a production key
    /// advertises. A wait that would exceed it means our model of the budget and Riot's have
    /// diverged badly; the call then proceeds rather than waiting indefinitely, takes a 429
    /// if it was wrong, and the response's own count headers resynchronise the window — which
    /// recovers, where an unbounded wait would simply stall the pipeline behind one call.
    /// </remarks>
    public int MaxPermitWaitSeconds { get; set; } = 600;
}
