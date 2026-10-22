using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using Ingestor.Options;
using Ingestor.Services;
using Microsoft.Extensions.Options;

namespace Ingestor.Riot.RateLimiting;

/// <summary>
/// Paces outbound Riot API calls against the limits Riot advertises, one budget per
/// routing value (#1359).
/// </summary>
/// <remarks>
/// <para>
/// Before #1359 the only pacing was reactive: send, take a 429, honour
/// <c>Retry-After</c>. The standard resilience handler's "rate limiter" is a
/// concurrency bulkhead, not a request-rate limiter, so nothing bounded the rate.
/// </para>
/// <para>
/// The decision "may I send now" is serialized per routing value by a lock. That is
/// deliberate: the application budget is a property of the routing value, so the decision
/// has to be taken one caller at a time to stay correct. The <em>wait</em> is not serialized
/// (#1457): a caller that may not send releases the lock, sleeps until its permit frees and
/// decides again. Holding the routing value while waiting made every caller queue behind
/// whoever happened to be first — harmless while one process ran at a time, starvation once
/// a permanent match-fetch worker keeps several acquisitions open on the same route. Regions
/// never wait on each other, which is the whole point — three regional budgets are three
/// times the throughput of the one the pipeline used to use.
/// </para>
/// <para>
/// On the regional routes, the decision is also taken per caller (#1457): the shares
/// <c>RiotRateLimit:ReservedShares</c> reserves are held back from everyone else while their
/// owner is active, and a caller can be held to a ceiling of its own
/// (<see cref="SetCallerCeiling"/>) — the permanent workers' target rate.
/// </para>
/// </remarks>
public interface IRiotRateLimiter
{
    /// <summary>
    /// Waits until the routing value's application budget — and, when enforced, the
    /// endpoint's method budget — permits one more request, then charges both.
    /// </summary>
    ValueTask AcquireAsync(string routingValue, string endpoint, CancellationToken cancellationToken);

    /// <summary>
    /// Feeds a response's rate-limit headers back into the budgets: the advertised
    /// limits, the counts Riot has recorded, and any 429 penalty.
    /// </summary>
    void Observe(string routingValue, string endpoint, HttpResponseMessage response);

    /// <summary>Describes the known budgets, for diagnostics.</summary>
    IReadOnlyList<RiotRateLimitSnapshot> Snapshot();

    /// <summary>
    /// Holds <paramref name="caller"/> to at most <paramref name="requestsPerMinute"/> requests
    /// a minute on every routing value it calls, or lifts its ceiling when null (#1457).
    /// </summary>
    void SetCallerCeiling(string caller, int? requestsPerMinute);
}

/// <inheritdoc cref="IRiotRateLimiter"/>
public sealed class RiotRateLimiter : IRiotRateLimiter, IDisposable
{
    private const string AppLimitHeader = "X-App-Rate-Limit";
    private const string AppCountHeader = "X-App-Rate-Limit-Count";
    private const string MethodLimitHeader = "X-Method-Rate-Limit";
    private const string MethodCountHeader = "X-Method-Rate-Limit-Count";
    private const string LimitTypeHeader = "X-Rate-Limit-Type";

    // The regional routing values, as the classifier reports them. The reservations are
    // scoped to these: they are the routes the permanent workers live on (#1457), and the
    // platform routes have neither the contention nor a caller that asked for a share.
    private static readonly FrozenSet<string> RegionalRoutingValues =
        new[] { "americas", "europe", "asia", "sea" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, double> NoReservations = new Dictionary<string, double>();

    private readonly ConcurrentDictionary<string, RoutingValueBudget> _budgets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _callerCeilings = new(StringComparer.Ordinal);
    private readonly RiotRateLimitOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IngestorMetrics _metrics;
    private readonly ICallerContext _callerContext;
    private readonly ILogger<RiotRateLimiter> _logger;

    public RiotRateLimiter(
        IOptions<RiotRateLimitOptions> options,
        TimeProvider timeProvider,
        IngestorMetrics metrics,
        ICallerContext callerContext,
        ILogger<RiotRateLimiter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _timeProvider = timeProvider;
        _metrics = metrics;
        _callerContext = callerContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public void SetCallerCeiling(string caller, int? requestsPerMinute)
    {
        if (requestsPerMinute is { } limit && limit > 0)
        {
            _callerCeilings[caller] = limit;
        }
        else
        {
            _callerCeilings.TryRemove(caller, out _);
        }
    }

    /// <inheritdoc />
    public async ValueTask AcquireAsync(string routingValue, string endpoint, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || string.IsNullOrEmpty(routingValue))
        {
            return;
        }

        var budget = _budgets.GetOrAdd(routingValue, _ => new RoutingValueBudget(_options.AppLimits));
        var method = _options.EnforceMethodLimits ? budget.MethodBucket(endpoint) : null;
        var caller = _callerContext.CurrentCaller;
        var shares = RegionalRoutingValues.Contains(routingValue)
            ? (IReadOnlyDictionary<string, double>)_options.ReservedShares
            : NoReservations;

        // Registered for the whole acquisition, wait included: a reserved caller that is
        // waiting is exactly the one whose share must be held back from the others.
        budget.EnterPending(caller);
        try
        {
            var totalWaited = TimeSpan.Zero;
            while (true)
            {
                var wait = budget.TryIssue(
                    routingValue,
                    endpoint,
                    caller,
                    method,
                    shares,
                    CeilingOf(caller),
                    this,
                    totalWaited);

                if (wait <= TimeSpan.Zero)
                {
                    if (totalWaited > TimeSpan.Zero)
                    {
                        _metrics.RecordRiotRateLimitWait(routingValue, endpoint, totalWaited);
                    }

                    return;
                }

                totalWaited += wait;
                await Task.Delay(wait, _timeProvider, cancellationToken);
            }
        }
        finally
        {
            budget.ExitPending(caller);
        }
    }

    /// <inheritdoc />
    public void Observe(string routingValue, string endpoint, HttpResponseMessage response)
    {
        if (!_options.Enabled || string.IsNullOrEmpty(routingValue) || response is null)
        {
            return;
        }

        var budget = _budgets.GetOrAdd(routingValue, _ => new RoutingValueBudget(_options.AppLimits));
        var now = _timeProvider.GetUtcNow();

        budget.Application.ApplyAdvertisedLimits(Header(response, AppLimitHeader));
        budget.Application.SyncObservedCounts(Header(response, AppCountHeader), now);

        var method = _options.EnforceMethodLimits ? budget.MethodBucket(endpoint) : null;
        if (method is not null)
        {
            method.ApplyAdvertisedLimits(Header(response, MethodLimitHeader));
            method.SyncObservedCounts(Header(response, MethodCountHeader), now);
        }

        if (response.StatusCode != System.Net.HttpStatusCode.TooManyRequests)
        {
            return;
        }

        var limitType = Header(response, LimitTypeHeader);
        var retryAfter = RetryAfter(response);

        // "method" penalises just that endpoint. Everything else — "application",
        // "service", or a 429 with no type at all — penalises the whole routing value:
        // a service-level throttle is not ours to attribute, and guessing narrowly
        // would keep hammering the budget that is actually exhausted.
        if (string.Equals(limitType, "method", StringComparison.OrdinalIgnoreCase) && method is not null)
        {
            method.ApplyPenalty(retryAfter, now);
        }
        else
        {
            budget.Application.ApplyPenalty(retryAfter, now);
        }

        _metrics.RecordRiotRateLimitRejection(routingValue, endpoint, limitType ?? "unknown");
        _logger.RateLimitHit(routingValue, endpoint, limitType ?? "unknown", retryAfter.TotalSeconds);
    }

    /// <inheritdoc />
    public IReadOnlyList<RiotRateLimitSnapshot> Snapshot()
    {
        var now = _timeProvider.GetUtcNow();
        return _budgets
            .Select(entry => new RiotRateLimitSnapshot(
                entry.Key,
                entry.Value.Application.DescribeWindows(),
                entry.Value.Application.PenaltyUntil > now ? entry.Value.Application.PenaltyUntil : null,
                entry.Value.MethodBudgetCount))
            .OrderBy(snapshot => snapshot.RoutingValue, StringComparer.Ordinal)
            .ToList();
    }

    // Nothing left to release since the decision moved from a semaphore to a lock (#1457);
    // kept so the limiter's owners still end its budgets with the scope that created it.
    public void Dispose() => _budgets.Clear();

    private TimeSpan RetryAfter(HttpResponseMessage response)
    {
        var delta = response.Headers.RetryAfter?.Delta;
        if (delta is { } value && value > TimeSpan.Zero)
        {
            return value;
        }

        if (response.Headers.TryGetValues("Retry-After", out var raw)
            && int.TryParse(raw.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.FromSeconds(_options.DefaultRetryAfterSeconds);
    }

    private static string? Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private int? CeilingOf(string? caller)
        => caller is not null && _callerCeilings.TryGetValue(caller, out var limit) ? limit : null;

    /// <summary>
    /// One routing value's budgets: the application bucket every endpoint shares, the
    /// per-endpoint method buckets, the per-caller ledger, and the lock that serializes the
    /// decision across them.
    /// </summary>
    private sealed class RoutingValueBudget(string configuredAppLimits)
    {
        private readonly ConcurrentDictionary<string, RiotRateLimitBucket> _methods = new(StringComparer.Ordinal);
        private readonly Lock _decision = new();
        private readonly RiotCallerLedger _ledger = new();

        public RiotRateLimitBucket Application { get; } = CreateApplication(configuredAppLimits);

        public int MethodBudgetCount => _methods.Count;

        public RiotRateLimitBucket MethodBucket(string endpoint)
            => _methods.GetOrAdd(endpoint, _ => new RiotRateLimitBucket());

        public void EnterPending(string? caller)
        {
            if (caller is null)
            {
                return;
            }

            lock (_decision)
            {
                _ledger.EnterPending(caller);
            }
        }

        public void ExitPending(string? caller)
        {
            if (caller is null)
            {
                return;
            }

            lock (_decision)
            {
                _ledger.ExitPending(caller);
            }
        }

        /// <summary>
        /// Issues one request for <paramref name="caller"/> if every budget allows it, and
        /// returns zero; otherwise issues nothing and returns how long to wait before
        /// deciding again.
        /// </summary>
        public TimeSpan TryIssue(
            string routingValue,
            string endpoint,
            string? caller,
            RiotRateLimitBucket? method,
            IReadOnlyDictionary<string, double> shares,
            int? ceiling,
            RiotRateLimiter limiter,
            TimeSpan alreadyWaited)
        {
            var options = limiter._options;
            lock (_decision)
            {
                var now = limiter._timeProvider.GetUtcNow();
                var linger = TimeSpan.FromSeconds(options.ReservationLingerSeconds);

                var heldBack = false;
                var wait = Application.TimeUntilPermitAvailable(
                    now,
                    options.SafetyHeadroom,
                    shares.Count == 0
                        ? null
                        : (duration, limit) =>
                        {
                            var hold = _ledger.HoldBackFor(caller, duration, limit, now, shares, linger);
                            heldBack |= hold > 0;
                            return hold;
                        });

                // A reservation can lapse long before the window frees a permit: its owner
                // goes idle once its run ends. Sleeping until the window's own expiry would
                // keep the share unused for up to a whole window, so a caller held back by
                // one decides again after at most a linger — that is what hands an unused
                // share back to whoever can use it.
                if (heldBack)
                {
                    wait = Min(wait, linger > TimeSpan.FromSeconds(1) ? linger : TimeSpan.FromSeconds(1));
                }

                if (method is not null)
                {
                    wait = Max(wait, method.TimeUntilPermitAvailable(now, options.SafetyHeadroom));
                }

                if (caller is not null)
                {
                    wait = Max(wait, _ledger.TimeUntilCeilingAllows(caller, ceiling, now));
                }

                var remainingBudget = TimeSpan.FromSeconds(options.MaxPermitWaitSeconds) - alreadyWaited;
                if (wait > remainingBudget)
                {
                    // Waiting longer than the configured ceiling means our model of the budget
                    // and Riot's have diverged badly. Proceeding takes a 429 if we were wrong,
                    // and the response's count headers resynchronise the window — which
                    // recovers, where an unbounded wait would stall the pipeline behind one
                    // call and recover nothing.
                    limiter._logger.PermitWaitCeilingExceeded(routingValue, endpoint, options.MaxPermitWaitSeconds);
                    wait = TimeSpan.Zero;
                }

                if (wait > TimeSpan.Zero)
                {
                    return wait;
                }

                Application.RecordIssued(now);
                method?.RecordIssued(now);
                if (caller is not null)
                {
                    _ledger.RecordIssued(caller, now, Application.LongestWindow);
                }

                return TimeSpan.Zero;
            }
        }

        private static RiotRateLimitBucket CreateApplication(string configuredAppLimits)
        {
            var application = new RiotRateLimitBucket();
            application.ApplyAdvertisedLimits(configuredAppLimits);
            return application;
        }

        private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;

        private static TimeSpan Min(TimeSpan left, TimeSpan right) => left <= right ? left : right;
    }
}
