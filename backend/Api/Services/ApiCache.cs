using Microsoft.Extensions.Caching.Memory;

namespace TrueMain.Services;

/// <summary>
/// The single way an API read writes to the shared response cache.
/// </summary>
/// <remarks>
/// The shared <c>IMemoryCache</c> runs with a <c>SizeLimit</c> (see
/// <c>Program.cs</c>, 1024 units). A <c>Set</c> whose entry carries no
/// <c>Size</c> is <em>silently dropped</em>: no exception, no log, just a value
/// that never caches and a permanent miss storm. Nothing in the type system
/// enforces it, so this helper is what makes forgetting impossible — every
/// entry it hands out already has a size, and the default of one unit is the
/// count-based charge the limit was sized for (the growth axis is key
/// cardinality, not payload bytes). A read whose value is not "roughly one
/// response" passes its own <c>size</c> rather than open-coding the options.
/// </remarks>
internal static class ApiCache
{
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
