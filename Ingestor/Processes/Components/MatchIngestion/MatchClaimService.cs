using Data.Entities;
using Data.Repositories;

namespace Ingestor.Processes.Components.MatchIngestion;

public sealed class MatchClaimService(
    IDataSessionFactory sessionFactory,
    ILogger<MatchClaimService> logger) : IMatchClaimService
{
    public async Task<List<AccountKey>> ClaimAsync(
        IReadOnlyCollection<string> platforms,
        int batchSize,
        TimeSpan lease,
        CancellationToken ct)
    {
        await using var session = await sessionFactory.CreateAsync(ct);
        await using var transaction = await session.BeginTransactionAsync(ct);

        var accounts = await session.RiotAccounts.ClaimAccountsForMatchIngestAtomicallyAsync(
            platforms,
            batchSize,
            DateTime.UtcNow,
            lease,
            ct);

        var claimed = new List<AccountKey>(accounts.Count);
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

            claimed.Add(account);
        }

        await session.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return claimed;
    }
}
