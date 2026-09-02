namespace Ingestor.Riot.RateLimiting;

/// <summary>
/// One rate-limit budget: either the application budget of a routing value, or the
/// method budget of one endpoint on that routing value. Holds the sliding windows
/// Riot advertises for it plus the penalty a 429 imposed on it.
/// </summary>
/// <remarks>
/// Instances are shared across the ingestor's concurrent callers, so every member
/// takes the bucket's own lock. Acquisition is serialized further up by the routing
/// value's semaphore; the lock here is what keeps the header-driven writes (which
/// happen after a response, outside that semaphore) from tearing a window.
/// </remarks>
internal sealed class RiotRateLimitBucket
{
    private readonly Lock _gate = new();

    // Keyed by window duration: Riot advertises one permit count per duration, and a
    // re-advertisement replaces the count rather than adding a second window.
    private readonly Dictionary<TimeSpan, RiotRateLimitWindow> _windows = [];

    private DateTimeOffset _penaltyUntil = DateTimeOffset.MinValue;

    /// <summary>
    /// Declares the windows this bucket enforces, from a Riot limit header
    /// ("20:1,100:120") or from the configured fallback.
    /// </summary>
    /// <remarks>
    /// Riot's header states the <em>complete</em> budget, so a parsable header replaces
    /// the window set rather than merging into it. That is what lets a real budget
    /// supersede the configured guess: keeping the old windows would leave a seeded
    /// "1 per second" in force behind a key that actually allows 500 per 10 seconds, and
    /// the tighter of the two always wins. Windows whose duration survives keep their
    /// counters, so adopting a limit never forgets what has already been spent — and a
    /// header that parses to nothing (absent, empty, malformed) changes nothing at all,
    /// which is what keeps a bad header from silently removing every limit.
    /// </remarks>
    /// <param name="limitHeader">A Riot limit header value, or the configured fallback.</param>
    public void ApplyAdvertisedLimits(string? limitHeader)
    {
        var advertised = RiotRateLimitWindow.ParseHeader(limitHeader).ToList();
        if (advertised.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            var replacement = new Dictionary<TimeSpan, RiotRateLimitWindow>(advertised.Count);
            foreach (var (permitLimit, duration) in advertised)
            {
                if (_windows.TryGetValue(duration, out var existing))
                {
                    existing.UpdatePermitLimit(permitLimit);
                    replacement[duration] = existing;
                }
                else
                {
                    replacement[duration] = new RiotRateLimitWindow(permitLimit, duration);
                }
            }

            _windows.Clear();
            foreach (var (duration, window) in replacement)
            {
                _windows[duration] = window;
            }
        }
    }

    /// <summary>
    /// Reconciles this bucket's windows with the counts Riot reports
    /// ("6:1,53:120"), so a count we under-tracked is corrected before it becomes a 429.
    /// </summary>
    /// <param name="countHeader">A Riot count header value.</param>
    /// <param name="now">The instant the counts were observed at.</param>
    public void SyncObservedCounts(string? countHeader, DateTimeOffset now)
    {
        lock (_gate)
        {
            foreach (var (observedCount, duration) in RiotRateLimitWindow.ParseHeader(countHeader))
            {
                if (_windows.TryGetValue(duration, out var window))
                {
                    window.SyncObservedCount(observedCount, now);
                }
            }
        }
    }

    /// <summary>
    /// How long until this bucket permits one more request: the longest wait across
    /// its windows, and never less than the remaining 429 penalty.
    /// </summary>
    /// <param name="now">The instant the decision is taken at.</param>
    /// <param name="safetyHeadroom">Fraction of each advertised limit held back.</param>
    /// <returns>How long to wait before issuing, or zero.</returns>
    public TimeSpan TimeUntilPermitAvailable(DateTimeOffset now, double safetyHeadroom)
    {
        lock (_gate)
        {
            var wait = _penaltyUntil > now ? _penaltyUntil - now : TimeSpan.Zero;

            foreach (var window in _windows.Values)
            {
                // Headroom is applied to the advertised limit, never below one permit:
                // a bucket that can never issue would deadlock the pipeline.
                var effectiveLimit = Math.Max(1, (int)Math.Floor(window.PermitLimit * (1 - safetyHeadroom)));
                var windowWait = window.TimeUntilPermitAvailable(now, effectiveLimit);
                if (windowWait > wait)
                {
                    wait = windowWait;
                }
            }

            return wait;
        }
    }

    /// <summary>Records that a request was issued against every window of this bucket.</summary>
    /// <param name="now">The instant the request was issued at.</param>
    public void RecordIssued(DateTimeOffset now)
    {
        lock (_gate)
        {
            foreach (var window in _windows.Values)
            {
                window.RecordIssued(now);
            }
        }
    }

    /// <summary>
    /// Blocks this bucket until <paramref name="retryAfter"/> has elapsed, after a 429
    /// attributed to it. Extends an existing penalty, never shortens it.
    /// </summary>
    /// <param name="retryAfter">How long Riot asked us to wait.</param>
    /// <param name="now">The instant the 429 was observed at.</param>
    public void ApplyPenalty(TimeSpan retryAfter, DateTimeOffset now)
    {
        if (retryAfter <= TimeSpan.Zero)
        {
            return;
        }

        lock (_gate)
        {
            var until = now + retryAfter;
            if (until > _penaltyUntil)
            {
                _penaltyUntil = until;
            }
        }
    }

    /// <summary>Instant this bucket is penalised until, for diagnostics.</summary>
    public DateTimeOffset PenaltyUntil
    {
        get
        {
            lock (_gate)
            {
                return _penaltyUntil;
            }
        }
    }

    /// <summary>The windows this bucket enforces, as (permit limit, duration) pairs, for diagnostics.</summary>
    public IReadOnlyList<(int PermitLimit, TimeSpan Duration)> DescribeWindows()
    {
        lock (_gate)
        {
            return _windows.Values
                .Select(window => (window.PermitLimit, window.Duration))
                .OrderBy(window => window.Duration)
                .ToList();
        }
    }
}
