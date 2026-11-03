using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Health;

/// <summary>
/// The cockpit's one call, cached and single-flighted (#1427), and kept warm by the
/// <see cref="CockpitCacheRefresher"/> (#2305). Every open Health page and Overview strip
/// re-asks every 30 s (#1411), and a full evaluation is several grouped scans — on a host whose
/// Postgres is busy with the aggregate lane that took tens of seconds, the live refresh then
/// queued scan behind scan and the admin proxy timed out.
///
/// <para>
/// A 30-second-old verdict is still an honest one: the verdict is judged server-side and
/// carries its own <c>EvaluatedAtUtc</c>, which the cockpit prints, and nothing it reports
/// moves faster than the pipeline cadences it watches (minutes to hours).
/// </para>
/// </summary>
public sealed class CachedPipelineHealthQueryService(
    IPipelineHealthQueryService inner,
    IMemoryCache cache,
    TimeProvider? clock = null) : IPipelineHealthQueryService
{
    /// <summary>Matches the admin's live-refresh period, so one open cockpit is one evaluation per tick at most.</summary>
    internal static readonly TimeSpan ResponseCacheTtl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a failed evaluation is not re-run (#2132): one live-refresh tick. Without it,
    /// every open tab's next tick re-ran the pass that just failed — on a cold cache, another
    /// full statement timeout against a Postgres that was already struggling. The sub-signals
    /// degrade on their own, so what reaches this is a failure of the evaluation itself.
    /// </summary>
    internal static readonly TimeSpan FailureBackoffWindow = ResponseCacheTtl;

    // Static so concurrent requests, each with its own scoped instance, and the refresher
    // share one pass. The payload takes no parameter, so one key covers every caller.
    internal static readonly WarmPayload<PipelineHealthReadModel> Payload =
        new("ops:pipeline-health", ResponseCacheTtl, FailureBackoffWindow);

    public Task<PipelineHealthReadModel> GetAsync(CancellationToken ct)
        => Payload.GetAsync(
            cache,
            // Detached from any one caller's token: a cockpit tab closing mid-pass must not
            // cancel the evaluation the other tabs are waiting on. Every query in it is
            // bounded by the command timeout, and its result is cached instead of lost.
            () => inner.GetAsync(CancellationToken.None),
            clock ?? TimeProvider.System,
            ct,
            // The inner service reads through request-scoped DbContexts, so the request that
            // started the pass must outlive it (see RequestCoalescer).
            ownerAwaitsToCompletion: true);
}
