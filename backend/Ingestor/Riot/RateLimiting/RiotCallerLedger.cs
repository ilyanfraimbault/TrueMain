namespace Ingestor.Riot.RateLimiting;

/// <summary>
/// One routing value's view of who is spending its application budget: what each caller
/// issued, who is waiting for a permit right now, and the per-caller ceilings (#1457).
/// </summary>
/// <remarks>
/// <para>
/// This is what lets a permanent match-fetch worker share a regional route with the cadenced
/// processes that also need it. A caller with a <em>reserved share</em> has that fraction of
/// every application window held back from the others while it is active — waiting for a
/// permit, or issued one in the last <c>RiotRateLimit:ReservationLingerSeconds</c> — less
/// what it has already spent in the window. An idle reservation holds nothing, so its
/// share goes to whoever can use it.
/// </para>
/// <para>
/// Not thread-safe on its own: every member is called under the routing value's decision
/// lock in <see cref="RiotRateLimiter"/>, which is what keeps the counts and the decision
/// taken from them consistent.
/// </para>
/// </remarks>
internal sealed class RiotCallerLedger
{
    private static readonly TimeSpan CeilingWindow = TimeSpan.FromMinutes(1);

    private readonly Dictionary<string, CallerUsage> _callers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RiotRateLimitWindow> _ceilings = new(StringComparer.Ordinal);

    /// <summary>Marks <paramref name="caller"/> as waiting for a permit.</summary>
    public void EnterPending(string caller) => Usage(caller).Pending++;

    /// <summary>Clears one pending acquisition of <paramref name="caller"/>.</summary>
    public void ExitPending(string caller) => Usage(caller).Pending--;

    /// <summary>
    /// Records an issued request against <paramref name="caller"/>'s usage and its ceiling.
    /// </summary>
    /// <param name="caller">The caller the request was issued for.</param>
    /// <param name="now">The instant the request was issued at.</param>
    /// <param name="retention">How long an issue stays relevant: the longest application window.</param>
    public void RecordIssued(string caller, DateTimeOffset now, TimeSpan retention)
    {
        Usage(caller).RecordIssued(now, retention);
        if (_ceilings.TryGetValue(caller, out var ceiling))
        {
            ceiling.RecordIssued(now);
        }
    }

    /// <summary>
    /// The permits of a window <paramref name="caller"/> may not take: for every other
    /// reserved caller that is active, its reservation of the window less what it already
    /// spent in it. A caller's own reservation is never held back from it.
    /// </summary>
    public int HoldBackFor(
        string? caller,
        TimeSpan windowDuration,
        int effectiveLimit,
        DateTimeOffset now,
        IReadOnlyDictionary<string, double> reservedShares,
        TimeSpan linger)
    {
        var hold = 0;
        foreach (var (reservedCaller, share) in reservedShares)
        {
            if (share <= 0
                || string.Equals(reservedCaller, caller, StringComparison.Ordinal)
                || !_callers.TryGetValue(reservedCaller, out var usage)
                || !usage.IsActive(now, linger))
            {
                continue;
            }

            // Rounded up, so a configured share always reserves at least one permit of a
            // window: rounded down, a 5 % share of the 20-per-second window reserves nothing.
            var reserved = (int)Math.Ceiling(share * effectiveLimit);
            hold += Math.Max(0, reserved - usage.CountWithin(windowDuration, now));
        }

        return hold;
    }

    /// <summary>
    /// How long until <paramref name="caller"/>'s own ceiling permits one more request, or
    /// zero when it has none or is under it.
    /// </summary>
    public TimeSpan TimeUntilCeilingAllows(string caller, int? requestsPerMinute, DateTimeOffset now)
    {
        if (requestsPerMinute is not { } limit || limit <= 0)
        {
            _ceilings.Remove(caller);
            return TimeSpan.Zero;
        }

        if (!_ceilings.TryGetValue(caller, out var ceiling))
        {
            ceiling = new RiotRateLimitWindow(limit, CeilingWindow);
            _ceilings[caller] = ceiling;
        }

        ceiling.UpdatePermitLimit(limit);
        return ceiling.TimeUntilPermitAvailable(now, limit);
    }

    /// <summary>What <paramref name="caller"/> issued within <paramref name="duration"/>, for diagnostics and tests.</summary>
    public int CountWithin(string caller, TimeSpan duration, DateTimeOffset now)
        => _callers.TryGetValue(caller, out var usage) ? usage.CountWithin(duration, now) : 0;

    private CallerUsage Usage(string caller)
    {
        if (!_callers.TryGetValue(caller, out var usage))
        {
            usage = new CallerUsage();
            _callers[caller] = usage;
        }

        return usage;
    }

    private sealed class CallerUsage
    {
        // Issue instants, oldest first, trimmed to the longest application window: a subset
        // of what the application windows themselves hold, so bounded by the same limit.
        private readonly Queue<DateTimeOffset> _issued = new();

        private DateTimeOffset _lastIssued = DateTimeOffset.MinValue;

        public int Pending { get; set; }

        public bool IsActive(DateTimeOffset now, TimeSpan linger)
            => Pending > 0 || now - _lastIssued < linger;

        public void RecordIssued(DateTimeOffset now, TimeSpan retention)
        {
            while (_issued.Count > 0 && _issued.Peek() + retention <= now)
            {
                _issued.Dequeue();
            }

            _issued.Enqueue(now);
            _lastIssued = now;
        }

        public int CountWithin(TimeSpan duration, DateTimeOffset now)
            => _issued.Count(issued => issued + duration > now);
    }
}
