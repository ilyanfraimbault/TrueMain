using Microsoft.Extensions.Caching.Memory;

namespace TrueMain.Services.Ops;

/// <summary>
/// Keeps the admin cockpit's cached payloads warm (#2305): pipeline health, the data-quality
/// detectors, the Overview counters, the aggregation stats and the patch coverage are each
/// recomputed shortly before their cache goes stale, so the operator's click reads a value
/// instead of paying the cold path.
///
/// <para>
/// <b>Only what someone reads.</b> A payload is refreshed only while it was read within
/// <see cref="IdleWindow"/>, and never before its first read: an admin nobody has open costs
/// nothing. What an idle stretch leaves behind is the last value, served stale with its
/// computed-at time on the next visit while this loop catches up (see
/// <see cref="WarmPayload{T}"/>).
/// </para>
///
/// <para>
/// <b>One at a time.</b> Each tick refreshes at most one payload — the most overdue — and
/// waits for it, so the refreshes never pile onto Postgres together; they go through the
/// payload's own single-flight, so a refresh and a cold request never run the same
/// computation twice. A payload whose last pass failed is skipped until its backoff window
/// closes, and keeps serving its last good value meanwhile.
/// </para>
/// </summary>
internal sealed class CockpitCacheRefresher(
    IEnumerable<WarmPayloadTarget> targets,
    IMemoryCache cache,
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<CockpitCacheRefresher> logger) : BackgroundService
{
    /// <summary>How often the loop looks for a payload to refresh.</summary>
    internal static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

    /// <summary>A payload not read for this long is no longer refreshed.</summary>
    internal static readonly TimeSpan IdleWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Share of its TTL a value may reach before it is refreshed: early enough that the
    /// refresh lands before the value goes stale, with a tick to spare on the 30 s cockpit.
    /// </summary>
    internal const double RefreshAtTtlFraction = 0.75;

    private readonly IReadOnlyList<WarmPayloadTarget> _targets = [.. targets];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TickInterval, clock, stoppingToken);
                await RefreshNextAsync();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down.
        }
    }

    /// <summary>
    /// Refreshes the most overdue payload that is due, if any, and returns its key.
    /// Not cancellable: the pass borrows a scope this method owns, and every query in it is
    /// bounded by the command timeout.
    /// </summary>
    internal async Task<string?> RefreshNextAsync()
    {
        var target = NextDue(clock.GetUtcNow());
        if (target is null)
        {
            return null;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await target.RefreshAsync(scope.ServiceProvider);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The cached value stays as it was and the backoff window holds the next try.
            logger.LogWarning(ex, "Background refresh of {CacheKey} failed; its last value stays served", target.Payload.Key);
        }

        return target.Payload.Key;
    }

    private WarmPayloadTarget? NextDue(DateTimeOffset now)
    {
        WarmPayloadTarget? mostOverdue = null;
        var highestAgeRatio = 0d;
        foreach (var target in _targets)
        {
            var state = target.Payload.Peek(cache);
            if (state is null || now - state.LastReadAtUtc > IdleWindow || target.Payload.IsBackingOff(cache))
            {
                continue;
            }

            var ageRatio = (now - state.ComputedAtUtc) / target.Payload.Ttl;
            if (ageRatio >= RefreshAtTtlFraction && ageRatio > highestAgeRatio)
            {
                mostOverdue = target;
                highestAgeRatio = ageRatio;
            }
        }

        return mostOverdue;
    }
}

/// <summary>A payload the <see cref="CockpitCacheRefresher"/> keeps warm, and how to recompute it in a scope.</summary>
internal sealed record WarmPayloadTarget(IWarmPayload Payload, Func<IServiceProvider, Task> RefreshAsync);
