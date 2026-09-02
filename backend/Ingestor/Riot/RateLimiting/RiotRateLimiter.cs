using System.Collections.Concurrent;
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
/// Acquisition is serialized per routing value by a semaphore. That is deliberate:
/// the application budget is a property of the routing value, so the decision "may I
/// send now" has to be taken one caller at a time to stay correct, and serializing it
/// costs nothing when the sustained allowance is under one request per second. Regions
/// never wait on each other, which is the whole point — three regional budgets are
/// three times the throughput of the one the pipeline used to use.
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
}

/// <inheritdoc cref="IRiotRateLimiter"/>
public sealed class RiotRateLimiter : IRiotRateLimiter, IDisposable
{
    private const string AppLimitHeader = "X-App-Rate-Limit";
    private const string AppCountHeader = "X-App-Rate-Limit-Count";
    private const string MethodLimitHeader = "X-Method-Rate-Limit";
    private const string MethodCountHeader = "X-Method-Rate-Limit-Count";
    private const string LimitTypeHeader = "X-Rate-Limit-Type";

    private readonly ConcurrentDictionary<string, RoutingValueBudget> _budgets = new(StringComparer.OrdinalIgnoreCase);
    private readonly RiotRateLimitOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IngestorMetrics _metrics;
    private readonly ILogger<RiotRateLimiter> _logger;

    public RiotRateLimiter(
        IOptions<RiotRateLimitOptions> options,
        TimeProvider timeProvider,
        IngestorMetrics metrics,
        ILogger<RiotRateLimiter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _timeProvider = timeProvider;
        _metrics = metrics;
        _logger = logger;
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

        await budget.Gate.WaitAsync(cancellationToken);
        try
        {
            var totalWaited = TimeSpan.Zero;
            while (true)
            {
                var now = _timeProvider.GetUtcNow();
                var wait = budget.Application.TimeUntilPermitAvailable(now, _options.SafetyHeadroom);
                if (method is not null)
                {
                    var methodWait = method.TimeUntilPermitAvailable(now, _options.SafetyHeadroom);
                    if (methodWait > wait)
                    {
                        wait = methodWait;
                    }
                }

                if (wait <= TimeSpan.Zero)
                {
                    budget.Application.RecordIssued(now);
                    method?.RecordIssued(now);

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
            budget.Gate.Release();
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
        _logger.LogWarning(
            "Riot rate limit hit on {RoutingValue}/{Endpoint} (type {LimitType}); backing off {RetryAfterSeconds}s.",
            routingValue,
            endpoint,
            limitType ?? "unknown",
            retryAfter.TotalSeconds);
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

    public void Dispose()
    {
        foreach (var budget in _budgets.Values)
        {
            budget.Dispose();
        }

        _budgets.Clear();
    }

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

    /// <summary>
    /// One routing value's budgets: the application bucket every endpoint shares, the
    /// per-endpoint method buckets, and the semaphore that serializes acquisition
    /// across them.
    /// </summary>
    private sealed class RoutingValueBudget : IDisposable
    {
        private readonly ConcurrentDictionary<string, RiotRateLimitBucket> _methods = new(StringComparer.Ordinal);

        public RoutingValueBudget(string configuredAppLimits)
        {
            Application = new RiotRateLimitBucket();
            Application.ApplyAdvertisedLimits(configuredAppLimits);
        }

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public RiotRateLimitBucket Application { get; }

        public int MethodBudgetCount => _methods.Count;

        public RiotRateLimitBucket MethodBucket(string endpoint)
            => _methods.GetOrAdd(endpoint, _ => new RiotRateLimitBucket());

        public void Dispose() => Gate.Dispose();
    }
}
