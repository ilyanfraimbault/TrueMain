using Data.Entities;
using Data.Repositories;
using Ingestor.Processes.Components.Coverage;

namespace Ingestor.Processes.Components.MatchIngestion;

public sealed class MatchClaimService(
    IDataSessionFactory sessionFactory,
    IChampionCoverageProvider coverageProvider,
    TimeProvider timeProvider,
    ILogger<MatchClaimService> logger) : IMatchClaimService
{
    public async Task<List<AccountKey>> ClaimAsync(
        IReadOnlyCollection<string> platforms,
        int batchSize,
        double establishedMainShare,
        TimeSpan lease,
        CancellationToken ct)
    {
        await using var session = await sessionFactory.CreateAsync(ct);

        // Split the batch across platforms by coverage deficit before claiming anything
        // (#1150). Read outside the transaction: it is a read-only signal that only decides
        // how many slots each platform gets, and holding the claim transaction open across it
        // would widen the window in which another instance's claim races this one.
        var coverage = await coverageProvider.GetSnapshotAsync(session, ct);
        var quotas = PlatformBudgetAllocator.Allocate(platforms, batchSize, coverage);

        logger.LogInformation(
            "Claim allocation for a batch of {BatchSize}: {Quotas}.",
            batchSize,
            string.Join(", ", quotas
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => $"{entry.Key}={entry.Value} (deficit {coverage.MeanDeficit(entry.Key):P0})")));

        await using var transaction = await session.BeginTransactionAsync(ct);

        var accounts = await session.RiotAccounts.ClaimAccountsForMatchIngestAtomicallyAsync(
            quotas,
            batchSize,
            establishedMainShare,
            timeProvider.GetUtcNow().UtcDateTime,
            lease,
            ct);

        // One statement per distinct platform instead of one per account (#858, #1229).
        // The per-account loop this replaces issued up to BatchSize sequential UPDATEs
        // inside the claim transaction, widening the very race window the comment above
        // exists to shrink. No SaveChanges follows: the claim and this transition are
        // both set-based ExecuteUpdate statements that never touch the change tracker,
        // so the call that used to sit here only simulated a commit point.
        var candidateAccounts = await session.MainCandidates.SetStatusForAccountsAsync(
            accounts,
            MainCandidateStatus.Queued,
            MainCandidateStatus.Processing,
            ct);

        logger.LogDebug(
            "Moved queued candidates to Processing for {CandidateAccountCount} of {ClaimedAccountCount} claimed accounts.",
            candidateAccounts.Count,
            accounts.Count);

        await transaction.CommitAsync(ct);

        // Every claimed account is returned, including the established mains that have
        // no Queued candidate left to move to Processing (their candidates are already
        // Validated). Filtering on that transition — the pre-#900 behaviour — silently
        // dropped exactly the accounts the established-main arm of the claim exists to
        // refresh: the row was already flagged Processing, so it was neither ingested
        // nor reverted, and sat out its whole lease. ValidateAsync / RevertAsync both
        // no-op on zero candidates and still reset the account, so returning them is safe.
        return accounts;
    }
}
