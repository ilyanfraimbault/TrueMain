using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Coverage;

/// <summary>
/// The patch-coverage payload, cached and single-flighted (#2302). A full evaluation is a
/// set of grouped scans over the fold tables, and every visit to the page — or every tab
/// left open on it — used to pay for all of them again.
///
/// <para>
/// A payload a few minutes old is still an honest one: it carries its own
/// <c>EvaluatedAtUtc</c>, which the page prints, and nothing it reports moves faster than
/// the folds it watches, which run on an hours cadence.
/// </para>
/// </summary>
public sealed class CachedPatchCoverageQueryService(
    IPatchCoverageQueryService inner,
    IMemoryCache cache) : IPatchCoverageQueryService
{
    /// <summary>How long one evaluation is served before the next caller re-runs it.</summary>
    internal static readonly TimeSpan ResponseCacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a failed evaluation is not re-run (#2132). Each measurement degrades to
    /// <c>unknown</c> on its own, so what reaches this is a failure of the patch lists the
    /// whole page rests on; re-running it on every reload would keep a failing scan going.
    /// </summary>
    internal static readonly TimeSpan FailureBackoffWindow = TimeSpan.FromSeconds(30);

    // The payload takes no parameter, so one key covers every caller.
    private const string CacheKey = "ops:patch-coverage";

    // Static so concurrent requests, each with its own scoped instance, share one pass.
    private static readonly RequestCoalescer<PatchCoverageReadModel> Coalescer = new();

    public async Task<PatchCoverageReadModel> GetAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out PatchCoverageReadModel? cached) && cached is not null)
        {
            return cached;
        }

        return await Coalescer.GetOrJoinAsync(
            CacheKey,
            async () =>
            {
                // Re-check under the coalescer: this caller may have queued behind a pass
                // that has since finished and cached its answer.
                if (cache.TryGetValue(CacheKey, out PatchCoverageReadModel? justCached) && justCached is not null)
                {
                    return justCached;
                }

                // Detached from any one caller's token: a tab closing mid-pass must not
                // cancel the evaluation the others are waiting on. Every query in it is
                // bounded by the command timeout, and the result is cached instead of lost.
                var computed = await FailureBackoff.RunAsync(
                    cache,
                    CacheKey,
                    FailureBackoffWindow,
                    () => inner.GetAsync(CancellationToken.None));
                return cache.Store(CacheKey, computed, ResponseCacheTtl);
            },
            ct,
            // The inner service reads through a request-scoped DbContext, so the request that
            // started the pass must outlive it (see RequestCoalescer).
            ownerAwaitsToCompletion: true);
    }
}
