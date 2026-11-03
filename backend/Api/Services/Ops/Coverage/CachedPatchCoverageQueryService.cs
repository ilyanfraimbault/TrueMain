using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Coverage;

/// <summary>
/// The patch-coverage payload, cached and single-flighted (#2302), and kept warm by the
/// <see cref="CockpitCacheRefresher"/> (#2305). A full evaluation is a set of grouped scans
/// over the fold tables, and every visit to the page — or every tab left open on it — used to
/// pay for all of them again.
///
/// <para>
/// A payload a few minutes old is still an honest one: it carries its own
/// <c>EvaluatedAtUtc</c>, which the page prints, and nothing it reports moves faster than
/// the folds it watches, which run on an hours cadence.
/// </para>
/// </summary>
public sealed class CachedPatchCoverageQueryService(
    IPatchCoverageQueryService inner,
    IMemoryCache cache,
    TimeProvider? clock = null) : IPatchCoverageQueryService
{
    /// <summary>How long one evaluation is served as fresh before it is recomputed.</summary>
    internal static readonly TimeSpan ResponseCacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a failed evaluation is not re-run (#2132). Each measurement degrades to
    /// <c>unknown</c> on its own, so what reaches this is a failure of the patch lists the
    /// whole page rests on; re-running it on every reload would keep a failing scan going.
    /// </summary>
    internal static readonly TimeSpan FailureBackoffWindow = TimeSpan.FromSeconds(30);

    // Static so concurrent requests, each with its own scoped instance, and the refresher
    // share one pass. The payload takes no parameter, so one key covers every caller.
    internal static readonly WarmPayload<PatchCoverageReadModel> Payload =
        new("ops:patch-coverage", ResponseCacheTtl, FailureBackoffWindow);

    public Task<PatchCoverageReadModel> GetAsync(CancellationToken ct)
        => Payload.GetAsync(
            cache,
            // Detached from any one caller's token: a tab closing mid-pass must not cancel
            // the evaluation the others are waiting on. Every query in it is bounded by the
            // command timeout, and the result is cached instead of lost.
            () => inner.GetAsync(CancellationToken.None),
            clock ?? TimeProvider.System,
            ct,
            // The inner service reads through a request-scoped DbContext, so the request that
            // started the pass must outlive it (see RequestCoalescer).
            ownerAwaitsToCompletion: true);
}
