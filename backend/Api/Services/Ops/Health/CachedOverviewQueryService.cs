using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Health;

/// <summary>
/// The Overview's counters, cached and single-flighted like the cockpit beside them (#1427,
/// #2301), and kept warm by the <see cref="CockpitCacheRefresher"/> (#2305). The admin's
/// landing page asks for them on every visit and every manual refresh; nothing they report
/// moves faster than the pipeline cadences behind them, and the payload carries its own
/// <c>ComputedAtUtc</c>.
/// </summary>
public sealed class CachedOverviewQueryService(
    IOverviewQueryService inner,
    IMemoryCache cache,
    TimeProvider? clock = null) : IOverviewQueryService
{
    internal static readonly TimeSpan ResponseCacheTtl = TimeSpan.FromMinutes(2);

    /// <summary>A failed pass is not re-run for this long, so a refresh cannot stack scans (#2132).</summary>
    internal static readonly TimeSpan FailureBackoffWindow = TimeSpan.FromSeconds(30);

    // Static so concurrent requests, each with its own scoped instance, and the refresher
    // share one pass. The payload takes no parameter, so one key covers every caller.
    internal static readonly WarmPayload<OverviewReadModel> Payload =
        new("ops:stats-overview", ResponseCacheTtl, FailureBackoffWindow);

    public Task<OverviewReadModel> GetAsync(CancellationToken ct)
        => Payload.GetAsync(
            cache,
            // Detached from any one caller's token: a tab closing mid-pass must not cancel the
            // pass the others are waiting on. The inner service owns its contexts (one per
            // parallel read, from the factory), so no request scope has to outlive it.
            () => inner.GetAsync(CancellationToken.None),
            clock ?? TimeProvider.System,
            ct,
            ownerAwaitsToCompletion: false);
}
