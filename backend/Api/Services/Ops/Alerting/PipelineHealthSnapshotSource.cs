using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Ops;
using TrueMain.Services.Ops.Health;

namespace TrueMain.Services.Ops.Alerting;

/// <summary>The cockpit verdict as the alerter reads it (#2292).</summary>
internal interface IPipelineHealthSnapshotSource
{
    Task<PipelineHealthReadModel> GetAsync(CancellationToken ct);
}

/// <summary>
/// Reads the cockpit's own warm payload (#2305) — the very evaluation <c>/health</c> shows, never
/// a second one — through <see cref="WarmPayload{T}.GetUnreadAsync"/>: a value an open cockpit
/// keeps fresh costs nothing, an older one is recomputed once and left in the cache for the next
/// visit, and the alerter's reads never count as visits that would keep the background
/// refresher busy all day.
/// </summary>
internal sealed class WarmPipelineHealthSnapshotSource(
    IMemoryCache cache,
    IServiceScopeFactory scopeFactory,
    TimeProvider clock) : IPipelineHealthSnapshotSource
{
    public async Task<PipelineHealthReadModel> GetAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // The computation borrows this scope's DbContexts, so the scope outlives it: the
        // payload's single-flight awaits the pass to completion before this returns.
        await using var scope = scopeFactory.CreateAsyncScope();
        var uncached = scope.ServiceProvider.GetRequiredService<PipelineHealthQueryService>();
        return await CachedPipelineHealthQueryService.Payload.GetUnreadAsync(
            cache,
            () => uncached.GetAsync(CancellationToken.None),
            clock);
    }
}
