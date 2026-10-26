using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Stats;

/// <summary>
/// The Aggregation panel's payload, cached and single-flighted (#2303). Nothing it reports moves
/// faster than the aggregate lane's 20-minute cadence, the payload carries its own
/// <see cref="AggregationsReadModel.MeasuredAtUtc"/>, and a burst of refreshes then costs one
/// measurement instead of one per click.
/// </summary>
public sealed class CachedAggregationStatsQueryService(
    IAggregationStatsQueryService inner,
    IMemoryCache cache) : IAggregationStatsQueryService
{
    internal static readonly TimeSpan ResponseCacheTtl = TimeSpan.FromMinutes(2);

    // The payload takes no parameter, so one key covers every caller.
    private const string CacheKey = "ops:aggregation-stats";

    // Static so concurrent requests, each with its own scoped instance, share one pass.
    private static readonly RequestCoalescer<AggregationsReadModel> Coalescer = new();

    public async Task<AggregationsReadModel> GetAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out AggregationsReadModel? cached) && cached is not null)
        {
            return cached;
        }

        return await Coalescer.GetOrJoinAsync(
            CacheKey,
            async () =>
            {
                if (cache.TryGetValue(CacheKey, out AggregationsReadModel? justCached) && justCached is not null)
                {
                    return justCached;
                }

                // Detached from any one caller's token, so a tab closing mid-pass does not
                // cancel the measurement the others are waiting on.
                var computed = await inner.GetAsync(CancellationToken.None);
                return cache.Store(CacheKey, computed, ResponseCacheTtl);
            },
            ct,
            // The inner service reads through a request-scoped DbContext.
            ownerAwaitsToCompletion: true);
    }
}
