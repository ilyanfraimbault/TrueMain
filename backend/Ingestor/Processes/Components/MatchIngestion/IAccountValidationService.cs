using Data.Repositories;

namespace Ingestor.Processes.Components.MatchIngestion;

public interface IAccountValidationService
{
    /// <summary>
    /// Promotes the account's Processing candidates to Validated. Returns whether any
    /// row actually moved, so the run summary can count validated <em>accounts</em>
    /// (#1024) — the row count would over-count an account carrying one candidate per
    /// champion, and the funnel compares accounts against the accounts it queued.
    /// </summary>
    Task<bool> ValidateAsync(AccountKey account, CancellationToken ct);
    Task RevertAsync(AccountKey account, CancellationToken ct);

    /// <summary>
    /// Releases the claim on an account that cannot be ingested at all — a row whose
    /// <c>platform_id</c> does not parse — putting its candidates back to Queued like
    /// <see cref="RevertAsync"/> does, but <em>also</em> stamping
    /// <c>LastMatchIngestAtUtc</c>.
    /// <para>
    /// That stamp is the whole point (#1223). Claims are ordered never-ingested-first
    /// then oldest-ingested-first, so a revert — which deliberately leaves the stamp
    /// untouched so a transient failure is retried immediately — hands an unusable row
    /// the head of every subsequent batch, forever. The condition here is permanent, so
    /// the row must move to the back of the queue instead.
    /// </para>
    /// </summary>
    Task ReleaseUningestableAsync(AccountKey account, CancellationToken ct);
}
