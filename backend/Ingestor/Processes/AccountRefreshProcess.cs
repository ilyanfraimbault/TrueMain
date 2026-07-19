using System.Net;
using Core;
using Core.Lol.Identifiers;
using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Ranking;
using Ingestor.Riot;
using Ingestor.Riot.Dto;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public sealed class AccountRefreshProcess(
    ILogger<AccountRefreshProcess> logger,
    IRiotAccountClient riotAccountClient,
    IRiotPlatformClient riotPlatformClient,
    IDataSessionFactory sessionFactory,
    IRankSnapshotWriter rankSnapshotWriter,
    TimeProvider timeProvider,
    IOptions<AccountRefreshOptions> refreshOptions) : IIngestorProcess
{
    private const string SoloQueueType = "RANKED_SOLO_5x5";

    public string Name => "AccountRefresh";

    public async Task<object?> RunCoreAsync(CancellationToken ct)
    {
        var accounts = await LoadAccountsForRefreshAsync(ct);
        if (accounts.Count == 0)
        {
            logger.LogInformation("No riot accounts found for refresh.");
            return new { reason = "No riot accounts found for refresh.", selected = 0 };
        }

        var summary = await RefreshAccountsAsync(accounts, ct);
        logger.LogInformation(
            "Account refresh summary: selected={Selected}, profileUpdated={ProfileUpdated}, profileRecovered={ProfileRecovered}, profileInvalidated={ProfileInvalidated}, profileSkipped={ProfileSkipped}, profileFailed={ProfileFailed}, rankInserted={RankInserted}, rankUnchanged={RankUnchanged}, rankSkippedUnranked={RankSkippedUnranked}, rankSkippedFresh={RankSkippedFresh}, rankFailed={RankFailed}.",
            summary.Selected,
            summary.ProfileUpdated,
            summary.ProfileRecovered,
            summary.ProfileInvalidated,
            summary.ProfileSkipped,
            summary.ProfileFailed,
            summary.RankInserted,
            summary.RankUnchanged,
            summary.RankSkippedUnranked,
            summary.RankSkippedFresh,
            summary.RankFailed);

        return BuildSuccessPayload(summary);
    }

    private async Task<IReadOnlyList<AccountKey>> LoadAccountsForRefreshAsync(CancellationToken ct)
    {
        await using var session = await sessionFactory.CreateAsync(ct);
        var batchSize = Math.Max(1, refreshOptions.Value.BatchSize);
        var accounts = await session.RiotAccounts.GetAccountsForRefreshAsync(batchSize, ct);
        return accounts
            .Select(account => new AccountKey(account.PlatformId, account.Puuid))
            .ToList();
    }

    private async Task<RefreshSummary> RefreshAccountsAsync(
        IReadOnlyList<AccountKey> accounts,
        CancellationToken ct)
    {
        await using var session = await sessionFactory.CreateAsync(ct);
        var accountsByKey = await session.RiotAccounts.GetByKeysAsync(accounts, ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var summary = new RefreshSummary { Selected = accounts.Count };

        var accountIds = accountsByKey.Values.Select(a => a.Id).ToList();
        var latestByAccountId = await session.RankSnapshots.GetLatestForAccountsAsync(accountIds, ct);
        var rankFreshness = refreshOptions.Value.RankSyncFreshness;

        foreach (var account in accounts)
        {
            ct.ThrowIfCancellationRequested();
            if (!accountsByKey.TryGetValue(account, out var accountEntity))
            {
                summary.ProfileFailed++;
                continue;
            }

            await RefreshSingleAccountAsync(session, accountEntity, latestByAccountId, rankFreshness, nowUtc, summary, ct);
        }

        await session.SaveChangesAsync(ct);
        return summary;
    }

    private async Task RefreshSingleAccountAsync(
        IDataSession session,
        RiotAccount account,
        IReadOnlyDictionary<Guid, RankSnapshot> latestByAccountId,
        TimeSpan rankFreshness,
        DateTime nowUtc,
        RefreshSummary summary,
        CancellationToken ct)
    {
        if (!PlatformId.TryParse(account.PlatformId, out var platform))
        {
            logger.LogWarning(
                "Skipping riot account {Puuid}: invalid platform {PlatformId}.",
                account.Puuid,
                account.PlatformId);
            summary.ProfileSkipped++;
            return;
        }

        var region = platform.Route.ToRegional();
        try
        {
            var profile = await riotAccountClient.GetAccountByPuuidAsync(account.Puuid, region, ct);

            if (!string.IsNullOrWhiteSpace(profile.GameName))
            {
                account.GameName = profile.GameName;
            }

            account.TagLine = string.IsNullOrWhiteSpace(profile.TagLine) ? null : profile.TagLine;
            account.UpdatedAtUtc = nowUtc;
            account.LastProfileSyncAtUtc = nowUtc;
            summary.ProfileUpdated++;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // account-v1 by-puuid returned 404: the PUUID no longer resolves
            // (deleted/banned account, or a rotated PUUID). Try to recover the
            // account by its Riot ID before giving up.
            var outcome = await TryRecoverByRiotIdAsync(session, account, region, nowUtc, ct);
            switch (outcome)
            {
                case RecoveryOutcome.Recovered:
                    summary.ProfileRecovered++;
                    break;

                case RecoveryOutcome.RetryLater:
                    // Transient failure on the recovery lookup — keep the account
                    // Active and let the next cycle try again. Skip rank this time.
                    summary.ProfileFailed++;
                    return;

                case RecoveryOutcome.Unrecoverable:
                    // No usable Riot ID, or Riot ID also 404s: mark the row Invalid
                    // so it drops out of every selection and stops burning a request
                    // on the same dead PUUID every cycle. Kept for history, not deleted.
                    account.Status = RiotAccountStatus.Invalid;
                    account.UpdatedAtUtc = nowUtc;
                    summary.ProfileInvalidated++;
                    logger.LogWarning(
                        "Invalidated riot account {Platform}/{Puuid}: unresolvable by PUUID and by Riot ID.",
                        account.PlatformId,
                        account.Puuid);
                    return;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to refresh riot account {Platform}/{Puuid}.",
                account.PlatformId,
                account.Puuid);
            summary.ProfileFailed++;
        }

        // Rank ingestion is independent of the profile sync above: a 404 or
        // timeout on League-v4 must not block the GameName/TagLine update,
        // and vice versa. Both share the single SaveChangesAsync at the end
        // of RefreshAccountsAsync.

        // Skip the by-puuid call when DiscoveryProcess has already snapped
        // this account's rank in the current cycle (Master+ ladder scans).
        if (rankFreshness > TimeSpan.Zero
            && account.LastRankSyncAtUtc is { } lastSync
            && nowUtc - lastSync < rankFreshness)
        {
            summary.RankSkippedFresh++;
            return;
        }

        try
        {
            var entries = await riotPlatformClient.GetLeagueEntriesByPuuidAsync(platform, account.Puuid, ct);
            var solo = entries.FirstOrDefault(e =>
                string.Equals(e.QueueType, SoloQueueType, StringComparison.Ordinal));

            if (solo is null || string.IsNullOrEmpty(solo.Tier) || string.IsNullOrEmpty(solo.Rank))
            {
                summary.RankSkippedUnranked++;
                return;
            }

            latestByAccountId.TryGetValue(account.Id, out var last);
            var outcome = rankSnapshotWriter.Ingest(
                session,
                account,
                new RankSnapshotInput(solo.Tier, solo.Rank, solo.LeaguePoints, solo.Wins, solo.Losses),
                last,
                nowUtc);

            if (outcome == RankSnapshotOutcome.Inserted)
            {
                summary.RankInserted++;
            }
            else
            {
                summary.RankUnchanged++;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed rank refresh for {Platform}/{Puuid}.",
                account.PlatformId,
                account.Puuid);
            summary.RankFailed++;
        }
    }

    /// <summary>
    /// Recovers an account whose PUUID stopped resolving by looking it up via its
    /// Riot ID (account-v1 by-riot-id) and refreshing the stored PUUID/identity.
    /// </summary>
    private async Task<RecoveryOutcome> TryRecoverByRiotIdAsync(
        IDataSession session,
        RiotAccount account,
        RegionalRoute region,
        DateTime nowUtc,
        CancellationToken ct)
    {
        // Without a GameName + TagLine there is nothing to look the account up by.
        if (string.IsNullOrWhiteSpace(account.GameName) || string.IsNullOrWhiteSpace(account.TagLine))
        {
            return RecoveryOutcome.Unrecoverable;
        }

        RiotAccountDto? resolved;
        try
        {
            resolved = await riotAccountClient.GetByRiotIdAsync(account.GameName, account.TagLine, region, ct);
        }
        catch (Exception ex)
        {
            // A transport/auth/rate-limit failure on the recovery lookup is not
            // proof the account is gone — don't invalidate, retry next cycle.
            logger.LogWarning(
                ex,
                "Riot ID recovery lookup failed for {Platform}/{GameName}#{TagLine}; leaving account active.",
                account.PlatformId,
                account.GameName,
                account.TagLine);
            return RecoveryOutcome.RetryLater;
        }

        // by-riot-id returned 404 (null): the Riot ID no longer exists either.
        if (resolved is null || string.IsNullOrWhiteSpace(resolved.Puuid))
        {
            return RecoveryOutcome.Unrecoverable;
        }

        // If the recovered PUUID differs and already belongs to another row, this
        // account is a stale duplicate: invalidate it instead of colliding on the
        // unique PUUID index at SaveChanges (which would fail the whole batch).
        if (!string.Equals(resolved.Puuid, account.Puuid, StringComparison.Ordinal)
            && await session.RiotAccounts.ExistsByPuuidAsync(resolved.Puuid, ct))
        {
            logger.LogWarning(
                "Riot account {Platform}/{Puuid} recovered to PUUID {NewPuuid} already held by another row; invalidating the stale duplicate.",
                account.PlatformId,
                account.Puuid,
                resolved.Puuid);
            return RecoveryOutcome.Unrecoverable;
        }

        account.Puuid = resolved.Puuid;
        if (!string.IsNullOrWhiteSpace(resolved.GameName))
        {
            account.GameName = resolved.GameName;
        }

        account.TagLine = string.IsNullOrWhiteSpace(resolved.TagLine) ? null : resolved.TagLine;
        account.UpdatedAtUtc = nowUtc;
        account.LastProfileSyncAtUtc = nowUtc;
        return RecoveryOutcome.Recovered;
    }

    private static object BuildSuccessPayload(RefreshSummary summary)
    {
        return new
        {
            selected = summary.Selected,
            profileUpdated = summary.ProfileUpdated,
            profileRecovered = summary.ProfileRecovered,
            profileInvalidated = summary.ProfileInvalidated,
            profileSkipped = summary.ProfileSkipped,
            profileFailed = summary.ProfileFailed,
            rankInserted = summary.RankInserted,
            rankUnchanged = summary.RankUnchanged,
            rankSkippedUnranked = summary.RankSkippedUnranked,
            rankSkippedFresh = summary.RankSkippedFresh,
            rankFailed = summary.RankFailed
        };
    }

    private enum RecoveryOutcome
    {
        /// <summary>The account was re-resolved by Riot ID and its PUUID refreshed.</summary>
        Recovered,

        /// <summary>The recovery lookup failed transiently; keep the account and retry later.</summary>
        RetryLater,

        /// <summary>No usable Riot ID or the Riot ID no longer resolves; mark the account Invalid.</summary>
        Unrecoverable
    }

    private sealed class RefreshSummary
    {
        public int Selected { get; set; }
        public int ProfileUpdated { get; set; }
        public int ProfileRecovered { get; set; }
        public int ProfileInvalidated { get; set; }
        public int ProfileSkipped { get; set; }
        public int ProfileFailed { get; set; }
        public int RankInserted { get; set; }
        public int RankUnchanged { get; set; }
        public int RankSkippedUnranked { get; set; }
        public int RankSkippedFresh { get; set; }
        public int RankFailed { get; set; }
    }
}
