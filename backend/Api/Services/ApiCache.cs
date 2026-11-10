using Microsoft.Extensions.Caching.Memory;

namespace TrueMain.Services;

/// <summary>
/// The single way an API read writes to the shared response cache.
/// </summary>
/// <remarks>
/// The shared <c>IMemoryCache</c> runs with a <c>SizeLimit</c>
/// (<see cref="ApiCacheServiceCollectionExtensions.SizeLimit"/>). A <c>Set</c> whose entry carries no
/// <c>Size</c> is <em>silently dropped</em>: no exception, no log, just a value
/// that never caches and a permanent miss storm. Nothing in the type system
/// enforces it, so this helper is what makes forgetting impossible — every
/// entry it hands out already has a size, and the default of one unit is the
/// count-based charge the limit was sized for (the growth axis is key
/// cardinality, not payload bytes). A read whose value is not "roughly one
/// response" passes its own <c>size</c> rather than open-coding the options.
/// </remarks>
/// <summary>
/// How long a failed read is not run again, and what the failure is remembered under when not the
/// read's own key: a versioned read passes its unversioned key, so a read that failed is not
/// retried just because the version moved on.
/// </summary>
internal readonly record struct ReadBackoff(TimeSpan Window, string? Key = null);

internal static class ApiCache
{
    // Static because the reads are scoped: the point is to coalesce across concurrent
    // *requests*, which each get their own instance. Non-generic payload so one instance
    // serves every response type.
    private static readonly RequestCoalescer<object?> Reads = new();

    /// <summary>
    /// The one read-through door of the Api: answers <paramref name="key"/> from the cache,
    /// joins the pass already computing it, or runs <paramref name="compute"/> once for
    /// everyone waiting and caches its answer for <paramref name="lifetime"/>, sized.
    /// </summary>
    /// <param name="cache">The shared response cache.</param>
    /// <param name="key">Carries every input that changes the answer, a version included.</param>
    /// <param name="lifetime">How long the answer stands.</param>
    /// <param name="compute">
    /// The read. Run with a token that is never a single caller's: the pass is shared, so one
    /// request walking away must not cancel it for the others. The pass runs on the owning
    /// request's scoped services (its <c>DbContext</c>), so the owner waits for it to finish
    /// even when its own caller leaves (see <see cref="RequestCoalescer{TValue}"/>).
    /// </param>
    /// <param name="ct">The calling request's token: bounds its wait, not the pass.</param>
    /// <param name="size">What the entry charges against the cache's <c>SizeLimit</c>.</param>
    /// <param name="failureBackoff">
    /// When set, a failed read is not run again for its window: callers get a
    /// <see cref="ReadBackoffException"/> at once (see <see cref="FailureBackoff"/>). For reads
    /// too long to repeat.
    /// </param>
    /// <remarks>
    /// Presence, not truthiness: a read may answer a legitimate "nothing here" with null, and a
    /// cached null is still an answer, not a miss to recompute on every visit.
    /// </remarks>
    public static async Task<T> GetOrComputeAsync<T>(
        this IMemoryCache cache,
        string key,
        TimeSpan lifetime,
        Func<CancellationToken, Task<T>> compute,
        CancellationToken ct,
        int size = 1,
        ReadBackoff? failureBackoff = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(compute);

        if (cache.TryGetValue(key, out var cached))
        {
            return (T)cached!;
        }

        var computed = await Reads.GetOrJoinAsync(
            key,
            async () =>
            {
                // Re-check under the coalescer: this caller may have queued behind a pass
                // that has since finished and cached its answer.
                if (cache.TryGetValue(key, out var justCached))
                {
                    return justCached;
                }

                var value = failureBackoff is { } backoff
                    ? await FailureBackoff.RunAsync(cache, backoff.Key ?? key, backoff.Window, () => compute(CancellationToken.None))
                    : await compute(CancellationToken.None);
                return cache.Store(key, (object?)value, lifetime, size);
            },
            ct,
            ownerAwaitsToCompletion: true);
        return (T)computed!;
    }

    /// <summary>
    /// Cache entry options expiring <paramref name="ttl"/> from now and
    /// charging <paramref name="size"/> units (one by default).
    /// </summary>
    public static MemoryCacheEntryOptions Entry(TimeSpan ttl, int size = 1) => new()
    {
        AbsoluteExpirationRelativeToNow = ttl,
        Size = size,
    };

    /// <summary>
    /// Stores <paramref name="value"/> under <paramref name="key"/> with a
    /// correctly sized entry and returns it, so a read can end on
    /// <c>return cache.Store(key, response, Ttl);</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately not named <c>Set</c>: the framework already ships a
    /// <c>Set(key, value, TimeSpan)</c> extension that builds an entry
    /// <em>without</em> a size, and shadowing it would either be ambiguous or,
    /// worse, resolve to the sizeless one.
    /// </remarks>
    public static T Store<T>(this IMemoryCache cache, object key, T value, TimeSpan ttl, int size = 1)
    {
        cache.Set(key, value, Entry(ttl, size));
        return value;
    }
}

public static class ApiCacheServiceCollectionExtensions
{
    /// <summary>
    /// Bounds the shared cache so a crafted fan-out of distinct request shapes cannot grow it
    /// without limit. Entries are counted, not weighed: the growth axis is key cardinality. A warm
    /// pass alone fills several thousand entries, twice over while it replaces the version readers
    /// are on (#2425), so the bound sits well above that or the warm-up evicts what it warmed.
    /// </summary>
    public const long SizeLimit = 32768;

    public static IServiceCollection AddTrueMainApiCache(this IServiceCollection services)
        => services.AddMemoryCache(options => options.SizeLimit = SizeLimit);
}
