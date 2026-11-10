using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Data.Statics;

/// <summary>
/// The per-patch cache every static-data provider sits on: one load per patch per process, shared
/// by concurrent callers; a faulted load forgotten so the next call retries; a patch the source
/// has not published yet answered from its newest data, and probed again once
/// <see cref="FallbackRecheckInterval"/> has passed; bounded to <paramref name="maxPatches"/>.
/// </summary>
/// <remarks>
/// <para>
/// Sources publish a patch hours (Data Dragon) to days (CommunityDragon) after Riot ships it, so on
/// every patch day the first games of the new patch are read while its data does not exist yet.
/// The previous patch's data is a far better answer than none (#1107), but it must not stand for
/// the life of a process that runs for days (#2212): the recheck bounds it.
/// </para>
/// <para>
/// Loads run with <see cref="CancellationToken.None"/>: the in-flight task is shared, so one
/// caller's cancellation must not fail it for the others. Each caller's own token only bounds its
/// wait.
/// </para>
/// </remarks>
/// <param name="what">What is loaded, for the log ("item metadata").</param>
/// <param name="loadPatch">One patch's data, or null when the source has not published it.</param>
/// <param name="loadLatest">The source's newest published data.</param>
/// <param name="logger">Where a failed load and a patch-day fallback are logged, once each.</param>
/// <param name="timeProvider">The clock of the fallback recheck.</param>
/// <param name="maxPatches">Cached patches past which all but the newest data are dropped.</param>
public sealed class PerPatchStaticCache<T>(
    string what,
    Func<string, CancellationToken, Task<T?>> loadPatch,
    Func<CancellationToken, Task<T>> loadLatest,
    ILogger logger,
    TimeProvider timeProvider,
    int maxPatches = 16)
    where T : class
{
    /// <summary>How long a patch answered from the newest data stands before its own data is probed again.</summary>
    public static readonly TimeSpan FallbackRecheckInterval = TimeSpan.FromMinutes(30);

    /// <summary>The key of the source's newest data, asked for directly or as a fallback.</summary>
    private const string Latest = "latest";

    private readonly ConcurrentDictionary<string, Lazy<Task<Loaded>>> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// The data of <paramref name="patch"/> (<c>major.minor</c>), or the newest data when null.
    /// Never throws but on <paramref name="ct"/>'s cancellation.
    /// </summary>
    public async Task<StaticData<T>> GetAsync(string? patch, CancellationToken ct)
    {
        var key = patch ?? Latest;

        // Two attempts at most: the second runs against an entry this call just created, which
        // cannot itself be a stale fallback, so the loop terminates.
        for (var attempt = 0; ; attempt++)
        {
            var entry = Entry(key);
            Loaded loaded;
            try
            {
                loaded = await entry.Value.WaitAsync(ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A faulted load must not be remembered: the shared task would replay the original
                // failure long after its transient cause cleared. Logged once, by the load itself.
                Evict(key, entry);
                return StaticData<T>.Unavailable(exception.Message);
            }

            if (loaded.Value is not null)
            {
                return StaticData<T>.Available(loaded.Value);
            }

            if (attempt == 0 && timeProvider.GetUtcNow() - loaded.LoadedAtUtc >= FallbackRecheckInterval)
            {
                Evict(key, entry);
                continue;
            }

            var latest = await GetAsync(null, ct);
            return latest.HasValue ? StaticData<T>.Fallback(latest.Value) : latest;
        }
    }

    private Lazy<Task<Loaded>> Entry(string key)
    {
        if (!_cache.ContainsKey(key) && _cache.Count >= maxPatches)
        {
            foreach (var cached in _cache.Keys.Where(cached => cached != Latest))
            {
                _cache.TryRemove(cached, out _);
            }
        }

        return _cache.GetOrAdd(key, static (patch, cache) =>
            new Lazy<Task<Loaded>>(
                () => cache.LoadAsync(patch),
                LazyThreadSafetyMode.ExecutionAndPublication),
            this);
    }

    private void Evict(string key, Lazy<Task<Loaded>> entry)
        => ((ICollection<KeyValuePair<string, Lazy<Task<Loaded>>>>)_cache)
            .Remove(new KeyValuePair<string, Lazy<Task<Loaded>>>(key, entry));

    /// <summary>A patch's data, or null data when the source has not published it, and when that was read.</summary>
    private sealed record Loaded(T? Value, DateTimeOffset LoadedAtUtc);

    private async Task<Loaded> LoadAsync(string key)
    {
        try
        {
            var value = key == Latest
                ? await loadLatest(CancellationToken.None)
                : await loadPatch(key, CancellationToken.None);
            if (value is null)
            {
                logger.LogWarning(
                    "The source has not published patch {Patch} yet; {What} is served from its newest data.",
                    key,
                    what);
            }

            return new Loaded(value, timeProvider.GetUtcNow());
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "{What} unavailable for {Patch}.", what, key);
            throw;
        }
    }
}
