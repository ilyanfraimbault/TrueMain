using System.Globalization;

namespace Ingestor.Riot.RateLimiting;

/// <summary>
/// One Riot rate-limit window — "at most <c>PermitLimit</c> requests per
/// <c>Duration</c>" — tracked as a sliding window over the instants we issued a
/// request at.
/// </summary>
/// <remarks>
/// Riot enforces <em>fixed</em> windows that reset on a wall-clock boundary we
/// cannot observe, so a sliding window is deliberately stricter: it never allows
/// more than the limit in <em>any</em> interval of <see cref="Duration"/>, which
/// is a superset of what a fixed window forbids. The cost is a small amount of
/// unused headroom right after a reset; the benefit is that we cannot walk into a
/// 429 by straddling a boundary we guessed wrong.
/// </remarks>
internal sealed class RiotRateLimitWindow(int permitLimit, TimeSpan duration)
{
    // Issue instants, oldest first. Bounded by PermitLimit: RecordIssued trims
    // before it enqueues, so this never grows past the limit it enforces.
    private readonly Queue<DateTimeOffset> _issued = new();

    /// <summary>How long the window spans.</summary>
    public TimeSpan Duration { get; } = duration;

    /// <summary>How many requests the window allows, as last advertised by Riot.</summary>
    public int PermitLimit { get; private set; } = permitLimit;

    /// <summary>
    /// Raises or lowers the permit limit to what Riot advertises. A production key
    /// carries different numbers from a personal one, and they are discovered from
    /// the response headers rather than configured, so a key upgrade needs no deploy.
    /// </summary>
    public void UpdatePermitLimit(int permitLimit)
    {
        if (permitLimit > 0)
        {
            PermitLimit = permitLimit;
        }
    }

    /// <summary>
    /// How long until this window would allow one more request, or
    /// <see cref="TimeSpan.Zero"/> when it allows one now.
    /// </summary>
    /// <param name="now">The instant the decision is taken at.</param>
    /// <param name="effectiveLimit">
    /// The limit to enforce, which may sit below <see cref="PermitLimit"/> when a
    /// safety headroom is configured.
    /// </param>
    public TimeSpan TimeUntilPermitAvailable(DateTimeOffset now, int effectiveLimit)
    {
        Trim(now);
        if (_issued.Count < effectiveLimit)
        {
            return TimeSpan.Zero;
        }

        // The oldest issue instant is the one whose expiry frees a permit. Peek is
        // safe: Count >= effectiveLimit >= 1 here.
        var wait = _issued.Peek() + Duration - now;
        return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
    }

    /// <summary>Records that a request was issued at <paramref name="now"/>.</summary>
    /// <param name="now">The instant the request was issued at.</param>
    public void RecordIssued(DateTimeOffset now)
    {
        Trim(now);
        _issued.Enqueue(now);
    }

    /// <summary>
    /// Reconciles the window with the count Riot reports for it. Riot's counter is
    /// authoritative and ours is not: another process sharing the key, a request
    /// that never reached this limiter, or a fixed-window reset we mis-modelled all
    /// leave us under-counting. Only ever pads upwards — trusting a <em>lower</em>
    /// remote count would hand out permits Riot has already spent.
    /// </summary>
    /// <param name="observedCount">The count Riot reports for this window.</param>
    /// <param name="now">The instant the count was observed at.</param>
    public void SyncObservedCount(int observedCount, DateTimeOffset now)
    {
        Trim(now);
        for (var i = _issued.Count; i < observedCount; i++)
        {
            _issued.Enqueue(now);
        }
    }

    /// <summary>
    /// Parses a Riot rate-limit header value ("20:1,100:120") into its windows.
    /// Unparseable segments are skipped rather than throwing: the header is
    /// advisory, and a malformed one must not fail the call it arrived on.
    /// </summary>
    /// <param name="headerValue">The raw header value, or null.</param>
    /// <returns>Every parsable window the value declares.</returns>
    public static IEnumerable<(int PermitLimit, TimeSpan Duration)> ParseHeader(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            yield break;
        }

        foreach (var segment in headerValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = segment.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0 || separator == segment.Length - 1)
            {
                continue;
            }

            if (!int.TryParse(segment[..separator], NumberStyles.Integer, CultureInfo.InvariantCulture, out var permits)
                || !int.TryParse(segment[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                || permits <= 0
                || seconds <= 0)
            {
                continue;
            }

            yield return (permits, TimeSpan.FromSeconds(seconds));
        }
    }

    private void Trim(DateTimeOffset now)
    {
        while (_issued.Count > 0 && _issued.Peek() + Duration <= now)
        {
            _issued.Dequeue();
        }
    }
}
