using Data.Entities;
using Data.Repositories;

namespace Ingestor.Processes.Components.MatchIngestion;

public sealed class MatchClaimService(
    IDataSessionFactory sessionFactory,
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
        await using var transaction = await session.BeginTransactionAsync(ct);

        var accounts = await session.RiotAccounts.ClaimAccountsForMatchIngestAtomicallyAsync(
            platforms,
            batchSize,
            establishedMainShare,
            timeProvider.GetUtcNow().UtcDateTime,
            lease,
            ct);

        foreach (var account in accounts)
        {
            var updated = await session.MainCandidates
                .SetStatusForAccountAsync(
                    account.PlatformId,
                    account.Puuid,
                    MainCandidateStatus.Queued,
                    MainCandidateStatus.Processing,
                    ct);

            if (updated > 0)
            {
                logger.LogDebug(
                    "Claimed {Count} candidates for {Platform}/{Puuid}.",
                    updated,
                    account.PlatformId,
                    account.Puuid);
            }
        }

        await session.SaveChangesAsync(ct);
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
