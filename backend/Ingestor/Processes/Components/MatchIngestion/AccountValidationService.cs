using Data.Entities;
using Data.Logging;
using Data.Repositories;

namespace Ingestor.Processes.Components.MatchIngestion;

public sealed class AccountValidationService(
    IDataSessionFactory sessionFactory,
    TimeProvider timeProvider,
    ILogger<AccountValidationService> logger) : IAccountValidationService
{
    public async Task<bool> ValidateAsync(AccountKey account, CancellationToken ct)
    {
        await using var session = await sessionFactory.CreateAsync(ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var updated = await session.MainCandidates
            .MarkValidatedForAccountAsync(account.PlatformId, account.Puuid, nowUtc, ct);

        if (updated > 0)
        {
            // Named ops event (#444): an account's candidates surviving ingestion
            // as Validated is the milestone the operator watches for. Logged at
            // Information — the Mongo sink persists registered OpsEvents despite
            // its Warning floor, and /ops/logs can filter on the event name.
            logger.LogInformation(
                OpsEvents.CandidateValidated,
                "Validated {Count} candidates for {Platform}/{Puuid}.",
                updated,
                account.PlatformId,
                account.Puuid);
        }

        await session.RiotAccounts.UpdateLastMatchIngestAtAsync(account.PlatformId, account.Puuid, nowUtc, ct);
        await session.RiotAccounts.SetMatchIngestStatusAsync(account.PlatformId, account.Puuid, MatchIngestStatus.Idle, ct);
        await session.SaveChangesAsync(ct);

        return updated > 0;
    }

    public async Task ReleaseUningestableAsync(AccountKey account, CancellationToken ct)
    {
        await using var session = await sessionFactory.CreateAsync(ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        // The candidates themselves are not settled by this — the account was never
        // addressed — so they go back to Queued exactly as a revert would leave them.
        var updated = await session.MainCandidates
            .SetStatusForAccountAsync(
                account.PlatformId,
                account.Puuid,
                MainCandidateStatus.Processing,
                MainCandidateStatus.Queued,
                ct);

        if (updated > 0)
        {
            logger.LogDebug(
                "Released {Count} candidates back to Queued for uningestable {Platform}/{Puuid}.",
                updated,
                account.PlatformId,
                account.Puuid);
        }

        // The one thing RevertAsync must not do and this must: move the row off the head
        // of the claim ordering, since nothing about its condition will change (#1223).
        await session.RiotAccounts.UpdateLastMatchIngestAtAsync(account.PlatformId, account.Puuid, nowUtc, ct);
        await session.RiotAccounts.SetMatchIngestStatusAsync(account.PlatformId, account.Puuid, MatchIngestStatus.Idle, ct);
        await session.SaveChangesAsync(ct);
    }

    public async Task RevertAsync(AccountKey account, CancellationToken ct)
    {
        await using var session = await sessionFactory.CreateAsync(ct);
        var updated = await session.MainCandidates
            .SetStatusForAccountAsync(
                account.PlatformId,
                account.Puuid,
                MainCandidateStatus.Processing,
                MainCandidateStatus.Queued,
                ct);

        if (updated > 0)
        {
            logger.LogDebug(
                "Reverted {Count} candidates to Queued for {Platform}/{Puuid}.",
                updated,
                account.PlatformId,
                account.Puuid);
        }

        await session.RiotAccounts.SetMatchIngestStatusAsync(account.PlatformId, account.Puuid, MatchIngestStatus.Idle, ct);
        await session.SaveChangesAsync(ct);
    }
}
