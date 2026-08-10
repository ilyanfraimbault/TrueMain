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
}
