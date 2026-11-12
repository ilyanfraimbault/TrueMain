using Data.Pipeline;

namespace Ingestor.Processes.Components.MatchIngestion;

/// <summary>
/// Frees the account claims the previous incarnation of the fetch lane died holding (#1513).
/// Without it, the accounts a redeploy interrupted stayed <c>Processing</c> until the rest of
/// <c>MatchIngestion:ClaimLeaseMinutes</c> had elapsed, so the deploy cost the pipeline the pass
/// it interrupted plus whatever was left of the lease. If it fails, the lease-based reap at the
/// top of the next run does the same job.
/// </summary>
public sealed class MatchClaimBootRecovery(
    IMatchClaimService claimService,
    ILogger<MatchClaimBootRecovery> logger) : IStepBootRecovery
{
    /// <inheritdoc />
    public PipelineProcess Step => PipelineCatalog.MatchIngestion;

    /// <inheritdoc />
    public async Task RecoverAsync(CancellationToken ct)
    {
        var released = await claimService.ReleaseOrphanedClaimsAsync(ct);
        if (!released.IsEmpty)
        {
            logger.LogWarning(
                "Released {Candidates} candidate(s) and {Accounts} account claim(s) orphaned by the previous incarnation at startup.",
                released.Candidates,
                released.Accounts);
        }
    }
}
