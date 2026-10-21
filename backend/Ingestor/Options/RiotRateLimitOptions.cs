using System.ComponentModel.DataAnnotations;

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
    [Range(0d, 0.5, MaximumIsExclusive = true)]
    public double SafetyHeadroom { get; set; } = 0.05;

    /// <summary>
    /// Penalty applied to a bucket when Riot answers 429 without a <c>Retry-After</c>
    /// header, in seconds.
    /// </summary>
    [Range(1, 600)]
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
    [Range(1, 3600)]
    public int MaxPermitWaitSeconds { get; set; } = 600;

    /// <summary>
    /// Fraction of every application window on a regional route (<c>americas</c>,
    /// <c>europe</c>, <c>asia</c>, <c>sea</c>) held back for a caller, by process name, while
    /// that caller is active (#1457). The permanent match-fetch workers run on those routes
    /// all the time; this is what keeps them from starving the cadenced processes that also
    /// need them. An idle reservation holds nothing, so its share goes to whoever can use it.
    /// </summary>
    /// <remarks>
    /// Binding adds to these defaults rather than replacing them; set a caller to 0 to drop
    /// its reservation. On a personal key (95 permits per 2 minutes after the headroom) the
    /// defaults reserve 5 + 2 permits per window, so another caller keeps at most 44 a
    /// minute while both are active.
    /// </remarks>
    public Dictionary<string, double> ReservedShares { get; set; } = new(StringComparer.Ordinal)
    {
        ["AccountRefresh"] = 0.05,
        ["ManualSeed"] = 0.02
    };

    /// <summary>
    /// How long after its last request a reserved caller still counts as active, in seconds.
    /// Covers the gap between two calls of the same run — the database work in between — so
    /// a permanent worker cannot take the reservation back between them.
    /// </summary>
    [Range(0, 600)]
    public int ReservationLingerSeconds { get; set; } = 5;
}
