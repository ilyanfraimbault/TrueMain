using Data.Ops.Mongo;
using Data.Pipeline;

namespace Ingestor.Processes.Components.ManualSeed;

/// <summary>
/// Returns to <c>Pending</c> the seed requests the previous incarnation left <c>Resolving</c>
/// (#2474): killed between claim and terminal write, such a request never reached
/// <c>GetPendingAsync</c> again and kept answering the create endpoint as "already queued", so
/// its Riot ID could never be re-seeded.
/// </summary>
public sealed class SeedClaimBootRecovery(
    ISeedRequestStore store,
    ILogger<SeedClaimBootRecovery> logger) : IStepBootRecovery
{
    /// <inheritdoc />
    public PipelineProcess Step => PipelineCatalog.ManualSeed;

    /// <inheritdoc />
    public async Task RecoverAsync(CancellationToken ct)
    {
        var returned = await store.ReturnOrphanedClaimsAsync(ct);
        if (returned > 0)
        {
            logger.LogWarning(
                "Returned {SeedRequests} seed request(s) left Resolving by the previous incarnation to Pending at startup.",
                returned);
        }
    }
}
