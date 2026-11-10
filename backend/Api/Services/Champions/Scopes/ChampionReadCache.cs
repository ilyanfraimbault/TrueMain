using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.Services.Champions.Warmup;

namespace TrueMain.Services.Champions.Scopes;

/// <summary>
/// Cache + single-flight for the champion reads, keyed by aggregation version.
/// See <see cref="ChampionReadCache"/> for why the two are one thing.
/// </summary>
public interface IChampionReadCache
{
    /// <summary>
    /// Returns the cached answer for <paramref name="key"/>, joins the pass already
    /// computing it, or runs <paramref name="compute"/> once on behalf of everyone
    /// waiting.
    /// </summary>
    /// <param name="key">
    /// Identifies the answer, and must carry every input that changes it — champion,
    /// lane, patch, elo bracket, population. The aggregation version is added by the
    /// implementation; callers must not add a timestamp of their own.
    /// </param>
    /// <param name="compute">
    /// The read. Invoked at most once per concurrent group, and with a token that is
    /// never a single caller's: the result is shared, so one request walking away must
    /// not cancel it for the others.
    /// </param>
    /// <param name="ct">The calling request's token.</param>
    /// <param name="size">
    /// What the entry charges against the shared cache's <c>SizeLimit</c>. One unit —
    /// "roughly one response" — unless the value is something else entirely.
    /// </param>
    /// <param name="failureBackoff">
    /// When set, a failed read is not run again for this long, whatever the version: callers
    /// get a <see cref="ReadBackoffException"/> at once (see <see cref="FailureBackoff"/>).
    /// For a read too long to repeat, not for the ordinary ones.
    /// </param>
    Task<T> GetOrComputeAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> compute,
        CancellationToken ct,
        int size = 1,
        TimeSpan? failureBackoff = null);
}

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
/// (#1368): synergies 2.3 s, item timings 1.3 s, matchups 0.57 s —
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
/// <b>Keyed by aggregation version, not by a clock.</b> The reads served from the
/// aggregate tables only change when the ingestor rewrites them, once per aggregation
/// cycle (~1–2 h) and never in between, so expiring them every 60 s threw away answers
/// that were still exactly right. Every key carries the version token below, and those
/// entries survive until the ingestor actually publishes new numbers.
/// </para>
/// <para>
/// <b>The live folds are a weaker case, deliberately.</b> The trio synergies, the live
/// branch of matchups, mains-comparison and the composition selection read
/// <c>match_participants</c> directly, so their answers also
/// move with match ingestion — which since #1374 runs in a lane of its own, not with the
/// aggregation. The version token does not track that, so for these
/// <see cref="AbsoluteBackstop"/> is the real freshness bound: up to half an hour, where
/// it used to be a minute. That is the trade this exists to make — these are exactly the
/// reads that measured 2–5 s cold — and it is bounded staleness, never a wrong answer.
/// Scaling was one of them until part B of #1368 moved it onto an aggregate table of
/// its own, which the token now covers like the others; item timings was dropped
/// instead, having had no caller since #628.
/// </para>
/// <para>
/// <b>With the warm-up on, a reader's version is the published one</b> (#2425), not the stamp:
/// see <see cref="ChampionReadVersion"/>.
/// </para>
/// </remarks>
public sealed class ChampionReadCache(
    IChampionAggregationStamp stamp,
    IMemoryCache cache,
    ChampionReadVersion version) : IChampionReadCache
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

    /// <inheritdoc />
    public async Task<T> GetOrComputeAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> compute,
        CancellationToken ct,
        int size = 1,
        TimeSpan? failureBackoff = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(compute);

        return await cache.GetOrComputeAsync(
            ComposeKey(await GetAggregationVersionAsync(ct), key),
            version.EntryLifetime(AbsoluteBackstop),
            compute,
            ct,
            size,
            failureBackoff,
            backoffKey: key);
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
        if (version.Current is { } pinned)
        {
            return pinned;
        }

        return await cache.GetOrComputeAsync(
            VersionCacheKey,
            VersionTtl,
            async token => await stamp.GetLatestAsync(token) is { } aggregatedAt
                ? aggregatedAt.Ticks.ToString(CultureInfo.InvariantCulture)
                : EmptyVersion,
            ct);
    }
}
