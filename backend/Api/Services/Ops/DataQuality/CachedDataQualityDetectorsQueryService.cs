using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.DataQuality;

/// <summary>
/// The data-quality detector suite, cached and single-flighted, and kept warm by the
/// <see cref="CockpitCacheRefresher"/> (#2305). It used to run all five detectors on every
/// call; they move on the pipeline's cadence, and the payload carries its own
/// <c>EvaluatedAtUtc</c>, which the panel prints and judges every age against.
///
/// <para>
/// The per-champion freshness breakdown stays uncached: it is the explicit, heavier click the
/// panel offers on purpose, and its answer is wanted as of that click.
/// </para>
/// </summary>
public sealed class CachedDataQualityDetectorsQueryService(
    IDataQualityDetectorsQueryService inner,
    IMemoryCache cache,
    TimeProvider? clock = null) : IDataQualityDetectorsQueryService
{
    internal static readonly TimeSpan ResponseCacheTtl = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long a failed pass is not re-run (#2132). Each detector degrades to <c>unknown</c>
    /// on its own, so what reaches this is a failure of the suite as a whole.
    /// </summary>
    internal static readonly TimeSpan FailureBackoffWindow = TimeSpan.FromSeconds(30);

    // Static so concurrent requests, each with its own scoped instance, and the refresher
    // share one pass. The payload takes no parameter, so one key covers every caller.
    internal static readonly WarmPayload<DataQualityDetectorsReadModel> Payload =
        new("ops:data-quality-detectors", ResponseCacheTtl, FailureBackoffWindow);

    public Task<DataQualityDetectorsReadModel> GetDetectorsAsync(CancellationToken ct)
        => Payload.GetAsync(
            cache,
            // Detached from any one caller's token, so a tab closing mid-pass does not
            // cancel the suite the others are waiting on.
            () => inner.GetDetectorsAsync(CancellationToken.None),
            clock ?? TimeProvider.System,
            ct,
            // The detectors read through a request-scoped DbContext.
            ownerAwaitsToCompletion: true);

    public Task<AggregateFreshnessReadModel> GetAggregateFreshnessAsync(CancellationToken ct)
        => inner.GetAggregateFreshnessAsync(ct);
}
