using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops.Health;

/// <summary>
/// The cockpit's one call, cached and single-flighted (#1427). Every open Health page and
/// Overview strip re-asks every 30 s (#1411), and a full evaluation is several grouped scans
/// — on a host whose Postgres is busy with the aggregate lane that took tens of seconds, the
/// live refresh then queued scan behind scan and the admin proxy timed out.
///
/// <para>
/// A 30-second-old verdict is still an honest one: the verdict is judged server-side and
/// carries its own <c>EvaluatedAtUtc</c>, which the cockpit prints, and nothing it reports
/// moves faster than the pipeline cadences it watches (minutes to hours).
/// </para>
/// </summary>
public sealed class CachedPipelineHealthQueryService(
    IPipelineHealthQueryService inner,
    IMemoryCache cache) : IPipelineHealthQueryService
{
    /// <summary>Matches the admin's live-refresh period, so one open cockpit is one evaluation per tick at most.</summary>
    internal static readonly TimeSpan ResponseCacheTtl = TimeSpan.FromSeconds(30);

    // The payload takes no parameter, so one key covers every caller.
    private const string CacheKey = "ops:pipeline-health";

    // Static for the same reason as ChampionStatsQueryService's coalescer: the point is to
    // share one pass across concurrent requests, each of which gets its own scoped instance.
    private static readonly RequestCoalescer<PipelineHealthReadModel> Coalescer = new();

    public async Task<PipelineHealthReadModel> GetAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out PipelineHealthReadModel? cached) && cached is not null)
        {
            return cached;
        }

        return await Coalescer.GetOrJoinAsync(
            CacheKey,
            async () =>
            {
                // Re-check under the coalescer: this caller may have queued behind a pass
                // that has since finished and cached its answer.
                if (cache.TryGetValue(CacheKey, out PipelineHealthReadModel? justCached) && justCached is not null)
                {
                    return justCached;
                }

                // Detached from any one caller's token: a cockpit tab closing mid-pass must
                // not cancel the evaluation the other tabs are waiting on. The pass still
                // ends — every query in it is bounded by the command timeout — and its
                // result is cached for the next tick instead of being thrown away.
                var computed = await inner.GetAsync(CancellationToken.None);
                return cache.Store(CacheKey, computed, ResponseCacheTtl);
            },
            ct,
            // The inner service reads through request-scoped DbContexts, so the request that
            // started the pass must outlive it (see RequestCoalescer).
            ownerAwaitsToCompletion: true);
    }
}
