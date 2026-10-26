using Microsoft.Extensions.Caching.Memory;

namespace TrueMain.Services;

/// <summary>
/// One parameterless cockpit payload kept warm (#2305): cached, single-flighted, served stale
/// while a background pass revalidates it, and kept as its last good value when that pass
/// fails.
///
/// <para>
/// <b>Fresh, stale, absent.</b> A value younger than <see cref="Ttl"/> is fresh and served as
/// is. An older one is still served at once — its payload carries its own computed-at time,
/// which the admin prints, so it is never presented as live — and the
/// <c>CockpitCacheRefresher</c> recomputes it within a tick, because the read just marked it
/// wanted. Only a payload with no value at all (never computed, or unread for
/// <see cref="WarmPayloadRetention.Value"/>) makes the caller wait for the computation.
/// </para>
///
/// <para>
/// <b>One computation at a time.</b> The request path and the refresher go through the same
/// <see cref="RequestCoalescer{TValue}"/> key, so a cold request and a background refresh
/// share one pass instead of running two. A failed pass opens the <see cref="FailureBackoff"/>
/// window under the same key and leaves the previous value where it was.
/// </para>
///
/// <para>
/// The state lives in the shared <see cref="IMemoryCache"/> (one sized entry, see
/// <see cref="ApiCache"/>), not in this object, so an instance is a stateless descriptor that
/// can be a static of the decorator owning it.
/// </para>
/// </summary>
internal sealed class WarmPayload<T>(string key, TimeSpan ttl, TimeSpan failureBackoff) : IWarmPayload
    where T : class
{
    private readonly RequestCoalescer<T> _coalescer = new();

    public string Key { get; } = key;

    public TimeSpan Ttl { get; } = ttl;

    public TimeSpan FailureBackoffWindow { get; } = failureBackoff;

    /// <summary>
    /// The payload for a request: the cached value, fresh or stale, or — when there is none —
    /// the result of <paramref name="compute"/>, joined with any pass already running.
    /// </summary>
    /// <remarks>
    /// <c>ownerAwaitsToCompletion</c> is <see cref="RequestCoalescer{TValue}.GetOrJoinAsync"/>'s:
    /// set it when <paramref name="compute"/> borrows the calling request's scope.
    /// </remarks>
    public async Task<T> GetAsync(
        IMemoryCache cache,
        Func<Task<T>> compute,
        TimeProvider clock,
        CancellationToken ct,
        bool ownerAwaitsToCompletion)
    {
        var now = clock.GetUtcNow();
        if (TryGetEntry(cache, out var entry))
        {
            entry.MarkRead(now);
            return entry.Value;
        }

        return await _coalescer.GetOrJoinAsync(
            Key,
            async () =>
            {
                // Re-check under the coalescer: this caller may have queued behind a pass
                // that has since finished and cached its answer.
                if (TryGetEntry(cache, out var justStored))
                {
                    return justStored.Value;
                }

                return await ComputeAndStoreAsync(cache, compute, clock);
            },
            ct,
            ownerAwaitsToCompletion);
    }

    /// <summary>
    /// Recomputes the payload whatever its age and stores it, or joins the pass already
    /// running. A failure leaves the cached value untouched and opens the backoff window.
    /// </summary>
    public Task<T> RefreshAsync(IMemoryCache cache, Func<Task<T>> compute, TimeProvider clock)
        => _coalescer.GetOrJoinAsync(
            Key,
            () => ComputeAndStoreAsync(cache, compute, clock),
            CancellationToken.None,
            ownerAwaitsToCompletion: true);

    /// <summary>
    /// The payload for a background consumer that is not a reader (#2292, the health
    /// alerter): the cached value while it is still fresh, else a new computation stored for
    /// the next reader — without marking the payload read either way.
    /// </summary>
    /// <remarks>
    /// Not marking it read is the point. A read keeps the <c>CockpitCacheRefresher</c>
    /// recomputing the payload for its whole idle window, so a consumer asking every few
    /// minutes through <see cref="GetAsync"/> would keep it recomputed every few seconds, all
    /// day, with nobody looking. Through here the consumer costs one computation per call at
    /// most — none while an open cockpit already keeps the value fresh — and what it computes
    /// still warms the next visit.
    /// </remarks>
    public Task<T> GetUnreadAsync(IMemoryCache cache, Func<Task<T>> compute, TimeProvider clock)
    {
        if (TryGetFresh(cache, clock, out var fresh))
        {
            return Task.FromResult(fresh);
        }

        return _coalescer.GetOrJoinAsync(
            Key,
            async () => TryGetFresh(cache, clock, out var justStored)
                ? justStored
                : await ComputeAndStoreAsync(cache, compute, clock, countsAsRead: false),
            CancellationToken.None,
            ownerAwaitsToCompletion: true);
    }

    public WarmPayloadState? Peek(IMemoryCache cache)
        => TryGetEntry(cache, out var entry)
            ? new WarmPayloadState(entry.ComputedAtUtc, entry.LastReadAtUtc)
            : null;

    public bool IsBackingOff(IMemoryCache cache) => FailureBackoff.IsOpen(cache, Key);

    private async Task<T> ComputeAndStoreAsync(
        IMemoryCache cache,
        Func<Task<T>> compute,
        TimeProvider clock,
        bool countsAsRead = true)
    {
        var computed = await FailureBackoff.RunAsync(cache, Key, FailureBackoffWindow, compute, clock);
        var now = clock.GetUtcNow();

        // A refresh keeps the read time the payload had: the refresher's own pass is not a
        // reader, otherwise an unread payload would keep itself warm for ever. A first value
        // stored by a non-reader starts as never read, so the refresher leaves it alone too.
        var lastReadAt = TryGetEntry(cache, out var previous)
            ? previous.LastReadAtUtc
            : countsAsRead ? now : DateTimeOffset.MinValue;
        cache.Store(Key, new Entry(computed, now, lastReadAt), WarmPayloadRetention.Value);
        return computed;
    }

    private bool TryGetFresh(IMemoryCache cache, TimeProvider clock, out T value)
    {
        if (TryGetEntry(cache, out var entry) && clock.GetUtcNow() - entry.ComputedAtUtc < Ttl)
        {
            value = entry.Value;
            return true;
        }

        value = null!;
        return false;
    }

    private bool TryGetEntry(IMemoryCache cache, out Entry entry)
    {
        if (cache.TryGetValue(Key, out Entry? found) && found is not null)
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }

    private sealed class Entry(T value, DateTimeOffset computedAtUtc, DateTimeOffset lastReadAtUtc)
    {
        private long _lastReadAtTicks = lastReadAtUtc.UtcTicks;

        public T Value { get; } = value;

        public DateTimeOffset ComputedAtUtc { get; } = computedAtUtc;

        public DateTimeOffset LastReadAtUtc
            => new(Interlocked.Read(ref _lastReadAtTicks), TimeSpan.Zero);

        public void MarkRead(DateTimeOffset now) => Interlocked.Exchange(ref _lastReadAtTicks, now.UtcTicks);
    }
}

/// <summary>The scheduling view of a <see cref="WarmPayload{T}"/>, independent of its payload type.</summary>
internal interface IWarmPayload
{
    string Key { get; }

    TimeSpan Ttl { get; }

    /// <summary>When the cached value was computed and last read, or null when there is none.</summary>
    WarmPayloadState? Peek(IMemoryCache cache);

    /// <summary>Whether the last pass failed within its backoff window.</summary>
    bool IsBackingOff(IMemoryCache cache);
}

internal sealed record WarmPayloadState(DateTimeOffset ComputedAtUtc, DateTimeOffset LastReadAtUtc);

/// <summary>
/// The retention every <see cref="WarmPayload{T}"/> shares, held outside the generic type so it
/// is one value rather than one static per closed payload type.
/// </summary>
internal static class WarmPayloadRetention
{
    /// <summary>
    /// How long a value is kept, and served stale, after its last computation. Long enough
    /// that the first visit of the day still reads a value instantly: the refresher stops
    /// recomputing an unread payload, so this is what an idle night leaves behind.
    /// </summary>
    public static readonly TimeSpan Value = TimeSpan.FromHours(24);
}
