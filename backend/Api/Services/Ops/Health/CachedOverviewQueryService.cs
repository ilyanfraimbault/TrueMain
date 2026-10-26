using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Health;

/// <summary>
/// The Overview's counters, cached and single-flighted like the cockpit beside them (#1427,
/// #2301). The admin's landing page asks for them on every visit and every manual refresh;
/// nothing they report moves faster than the pipeline cadences behind them, and the payload
/// carries its own <c>ComputedAtUtc</c>.
/// </summary>
public sealed class CachedOverviewQueryService(
    IOverviewQueryService inner,
    IMemoryCache cache) : IOverviewQueryService
{
    internal static readonly TimeSpan ResponseCacheTtl = TimeSpan.FromMinutes(2);

    /// <summary>A failed pass is not re-run for this long, so a refresh cannot stack scans (#2132).</summary>
    internal static readonly TimeSpan FailureBackoffWindow = TimeSpan.FromSeconds(30);

    // The payload takes no parameter, so one key covers every caller.
    private const string CacheKey = "ops:stats-overview";

    // Static so concurrent requests, each with its own scoped instance, share one pass.
    private static readonly RequestCoalescer<OverviewReadModel> Coalescer = new();

    public async Task<OverviewReadModel> GetAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out OverviewReadModel? cached) && cached is not null)
        {
            return cached;
        }

        return await Coalescer.GetOrJoinAsync(
            CacheKey,
            async () =>
            {
                // Re-check under the coalescer: this caller may have queued behind a pass
                // that has since finished and cached its answer.
                if (cache.TryGetValue(CacheKey, out OverviewReadModel? justCached) && justCached is not null)
                {
                    return justCached;
                }

                // Detached from any one caller's token: a tab closing mid-pass must not cancel
                // the pass the others are waiting on. The inner service owns its contexts
                // (one per parallel read, from the factory), so no request scope has to
                // outlive it.
                var computed = await FailureBackoff.RunAsync(
                    cache,
                    CacheKey,
                    FailureBackoffWindow,
                    () => inner.GetAsync(CancellationToken.None));
                return cache.Store(CacheKey, computed, ResponseCacheTtl);
            },
            ct);
    }
}
