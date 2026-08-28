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

        // The three statements below are one work unit (#1229). They used to run bare:
        // a failure between the first and the last left the candidates Validated while
        // the account stayed Processing until its claim lease expired. All three are
        // set-based ExecuteUpdate calls that never enter the change tracker, so the
        // SaveChangesAsync that used to close this method committed nothing — it only
        // simulated a commit point. An explicit transaction is what actually makes them
        // atomic.
        int updated;
        await using (var transaction = await session.BeginTransactionAsync(ct))
        {
            updated = await session.MainCandidates
                .MarkValidatedForAccountAsync(account.PlatformId, account.Puuid, nowUtc, ct);

            await session.RiotAccounts.UpdateLastMatchIngestAtAsync(account.PlatformId, account.Puuid, nowUtc, ct);
            await session.RiotAccounts.SetMatchIngestStatusAsync(account.PlatformId, account.Puuid, MatchIngestStatus.Idle, ct);
            await transaction.CommitAsync(ct);
        }

        if (updated > 0)
        {
            // Named ops event (#444): an account's candidates surviving ingestion
            // as Validated is the milestone the operator watches for. Logged at
            // Information — the Mongo sink persists registered OpsEvents despite
            // its Warning floor, and /ops/logs can filter on the event name. Emitted
            // after the commit so it only ever reports a promotion that stuck.
            logger.LogInformation(
                OpsEvents.CandidateValidated,
                "Validated {Count} candidates for {Platform}/{Puuid}.",
                updated,
                account.PlatformId,
                account.Puuid);
        }

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

        // Same work unit as ValidateAsync, mirrored: releasing the candidates without
        // releasing the account is the failure mode that pins a claim for a whole lease.
        int updated;
        await using (var transaction = await session.BeginTransactionAsync(ct))
        {
            updated = await session.MainCandidates
                .SetStatusForAccountAsync(
                    account.PlatformId,
                    account.Puuid,
                    MainCandidateStatus.Processing,
                    MainCandidateStatus.Queued,
                    ct);

            await session.RiotAccounts.SetMatchIngestStatusAsync(account.PlatformId, account.Puuid, MatchIngestStatus.Idle, ct);
            await transaction.CommitAsync(ct);
        }

        if (updated > 0)
        {
            logger.LogDebug(
                "Reverted {Count} candidates to Queued for {Platform}/{Puuid}.",
                updated,
                account.PlatformId,
                account.Puuid);
        }
    }
}
