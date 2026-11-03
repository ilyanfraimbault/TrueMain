using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Stats;

/// <summary>
/// The Aggregation panel's payload, cached and single-flighted (#2303), and kept warm by the
/// <see cref="CockpitCacheRefresher"/> (#2305). Nothing it reports moves faster than the
/// aggregate lane's 20-minute cadence, the payload carries its own
/// <see cref="AggregationsReadModel.MeasuredAtUtc"/>, and a burst of refreshes then costs one
/// measurement instead of one per click.
/// </summary>
public sealed class CachedAggregationStatsQueryService(
    IAggregationStatsQueryService inner,
    IMemoryCache cache,
    TimeProvider? clock = null) : IAggregationStatsQueryService
{
    internal static readonly TimeSpan ResponseCacheTtl = TimeSpan.FromMinutes(2);

    /// <summary>A failed measurement is not re-run for this long, by a reader or by the refresher (#2132).</summary>
    internal static readonly TimeSpan FailureBackoffWindow = TimeSpan.FromSeconds(30);

    // Static so concurrent requests, each with its own scoped instance, and the refresher
    // share one pass. The payload takes no parameter, so one key covers every caller.
    internal static readonly WarmPayload<AggregationsReadModel> Payload =
        new("ops:aggregation-stats", ResponseCacheTtl, FailureBackoffWindow);

    public Task<AggregationsReadModel> GetAsync(CancellationToken ct)
        => Payload.GetAsync(
            cache,
            // Detached from any one caller's token, so a tab closing mid-pass does not
            // cancel the measurement the others are waiting on.
            () => inner.GetAsync(CancellationToken.None),
            clock ?? TimeProvider.System,
            ct,
            // The inner service reads through a request-scoped DbContext.
            ownerAwaitsToCompletion: true);
}
