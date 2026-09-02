using System.Globalization;
using Microsoft.Extensions.Caching.Memory;

namespace TrueMain.Services.Champions;

/// <summary>
/// The one way a champion read answers a request: from the shared cache, from the
/// pass someone else is already running, or — once, for everybody — from Postgres.
/// </summary>
/// <remarks>
/// <para>
/// Champion reads were caching individually, each with its own
/// <c>TryGetValue</c>/<c>Store</c> pair and a 60 s TTL, and several
/// (<c>ChampionBuildsQueryService</c>, the live matchup fold, the synergy trios, the
/// trend and patch-diff reads) were not caching at all. Measured cold on production
/// (#1368): roam 14.1 s, synergies 2.3 s, item timings 1.3 s, matchups 0.57 s —
/// against 0.08–0.15 s warm. With 173 champions × 5 lanes × rank brackets, a 60 s TTL
/// means practically every visit to a non-top champion pays the cold price, and
/// nothing stopped ten concurrent visitors from each paying it at the same time.
/// </para>
/// <para>
/// <b>One entry point, so a new read cannot forget half of it.</b> The two mechanisms
/// only work together: a cache without single-flight turns each expiry into a
/// stampede of identical 14-second scans, and single-flight without a cache re-runs
/// the pass for the next visitor. Going through
/// <see cref="GetOrComputeAsync{T}"/> is also what guarantees the entry carries a
/// <c>Size</c> — the shared <c>IMemoryCache</c> runs with a <c>SizeLimit</c> and
/// silently drops sizeless entries (see <see cref="ApiCache"/>).
/// <c>ChampionReadCacheRegistrationTests</c> pins that every champion query service
/// the controller resolves takes this and not a raw <c>IMemoryCache</c>.
/// </para>
/// <para>
/// <b>Keyed by aggregation version, not by a clock.</b> These answers are folds over
/// data the ingestor rewrites once per aggregation cycle (~1–2 h) and never in
/// between, so expiring them every 60 s threw away answers that were still exactly
/// right. Every key instead carries the version token below, and entries survive
/// until the ingestor actually publishes new numbers.
/// <see cref="AbsoluteBackstop"/> remains as a backstop — not for freshness, but so a
/// version token that somehow stops moving cannot pin a stale answer for ever, and so
/// the cache's own bookkeeping keeps turning over.
/// </para>
/// </remarks>
public sealed class ChampionReadCache(IChampionAggregationStamp stamp, IMemoryCache cache) : IChampionReadCache
{
    /// <summary>Cache key holding the current aggregation version token.</summary>
    internal const string VersionCacheKey = "champions:aggregation-version";

    /// <summary>
    /// How long the version token itself is trusted. The token read has to be cheap
    /// enough not to become the very hot query this class exists to remove: at 5 s,
    /// and single-flighted like everything else here, it costs at most one
    /// <c>max()</c> over <c>champion_aggregate_scopes</c> every five seconds no matter
    /// how much traffic arrives — while never letting a finished aggregation cycle go
    /// unnoticed for longer than that.
    /// </summary>
    internal static readonly TimeSpan VersionTtl = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Upper bound on how long one cached answer may live, regardless of version.
    /// </summary>
    internal static readonly TimeSpan AbsoluteBackstop = TimeSpan.FromMinutes(30);

    /// <summary>The token used when nothing has been aggregated yet.</summary>
    internal const string EmptyVersion = "none";

    // Static because the services are scoped: the point is to coalesce across
    // concurrent *requests*, which each get their own instance. Non-generic payload
    // so one instance serves every response type that goes through here.
    private static readonly RequestCoalescer<object?> ReadCoalescer = new();
    private static readonly RequestCoalescer<string> VersionCoalescer = new();

    /// <inheritdoc />
    public async Task<T> GetOrComputeAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> compute,
        CancellationToken ct,
        int size = 1)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(compute);

        var versionedKey = ComposeKey(await GetAggregationVersionAsync(ct), key);

        // Presence, not truthiness: several of these reads answer a legitimate "no data
        // for this slice" with null (an unplayed matchup, a lane a champion never
        // takes), and those are exactly the slices nobody has warmed. Treating a cached
        // null as a miss would leave the emptiest corners of the site re-scanning on
        // every visit. Only this key writes this entry, so the cast is safe.
        if (cache.TryGetValue(versionedKey, out var cached))
        {
            return (T)cached!;
        }

        var computed = await ReadCoalescer.GetOrJoinAsync(
            versionedKey,
            async () =>
            {
                // Re-check under the coalescer: this caller may have queued behind a
                // pass that has since finished and cached its answer.
                if (cache.TryGetValue(versionedKey, out var justCached))
                {
                    return justCached;
                }

                // Detached from any one caller's token: the pass is shared, and the
                // owner is holding its request open for it (ownerAwaitsToCompletion),
                // so cancelling here would only throw away work everyone is waiting
                // for. The bound is the command timeout, as it is for every read here.
                var value = await compute(CancellationToken.None);
                return cache.Store(versionedKey, (object?)value, AbsoluteBackstop, size);
            },
            ct,
            // These reads run on the caller's request-scoped DbContext, so the pass
            // dies with the owning request. See RequestCoalescer.
            ownerAwaitsToCompletion: true);

        return (T)computed!;
    }

    /// <summary>
    /// The cache key a read is actually stored under: the caller's key stamped with
    /// the aggregation version, so publishing new aggregates retires every entry at
    /// once without anyone having to enumerate or evict them.
    /// </summary>
    internal static string ComposeKey(string version, string key) => $"{key}@v{version}";

    /// <summary>
    /// A token that changes exactly when the ingestor's aggregation lane publishes new
    /// numbers: the newest <c>AggregatedAtUtc</c> over <c>champion_aggregate_scopes</c>,
    /// which the pattern aggregation stamps on every scope it writes.
    /// </summary>
    private async Task<string> GetAggregationVersionAsync(CancellationToken ct)
    {
        if (cache.TryGetValue<string>(VersionCacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        return await VersionCoalescer.GetOrJoinAsync(
            VersionCacheKey,
            async () =>
            {
                if (cache.TryGetValue<string>(VersionCacheKey, out var justCached) && justCached is not null)
                {
                    return justCached;
                }

                var latest = await stamp.GetLatestAsync(CancellationToken.None);

                var token = latest is { } aggregatedAt
                    ? aggregatedAt.Ticks.ToString(CultureInfo.InvariantCulture)
                    : EmptyVersion;

                return cache.Store(VersionCacheKey, token, VersionTtl);
            },
            ct,
            ownerAwaitsToCompletion: true);
    }
}
