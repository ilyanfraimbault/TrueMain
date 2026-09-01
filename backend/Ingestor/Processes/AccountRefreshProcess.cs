using System.Net;
using Core;
using Core.Lol.Identifiers;
using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Summaries;
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

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var accounts = await LoadAccountsForRefreshAsync(ct);
        if (accounts.Count == 0)
        {
            logger.LogInformation("No riot accounts found for refresh.");
            return new NoWorkSummary("No riot accounts found for refresh.", 0);
        }

        var summary = await RefreshAccountsAsync(accounts, ct);
        logger.LogInformation(
            "Account refresh summary: selected={Selected}, profileUpdated={ProfileUpdated}, profileRecovered={ProfileRecovered}, profileInvalidated={ProfileInvalidated}, profileSkipped={ProfileSkipped}, profileFailed={ProfileFailed}, rankInserted={RankInserted}, rankUpdated={RankUpdated}, rankUnchanged={RankUnchanged}, rankSkippedUnranked={RankSkippedUnranked}, rankSkippedFresh={RankSkippedFresh}, rankFailed={RankFailed}.",
            summary.Selected,
            summary.ProfileUpdated,
            summary.ProfileRecovered,
            summary.ProfileInvalidated,
            summary.ProfileSkipped,
            summary.ProfileFailed,
            summary.RankInserted,
            summary.RankUpdated,
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
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var summary = new RefreshSummary { Selected = accounts.Count };
        var rankFreshness = refreshOptions.Value.RankSyncFreshness;
        var saveBatchSize = Math.Max(1, refreshOptions.Value.SaveBatchSize);

        // PUUIDs handed out by Riot-ID recovery during this run. Nothing below is
        // persisted before each slice's SaveChangesAsync, so the database check in
        // TryRecoverByRiotIdAsync cannot see a sibling account of the same slice
        // that already recovered to the same PUUID (#1223). A slice that already
        // committed is visible to the database check in later slices, so this set
        // only needs to catch collisions within the slice still in flight.
        var claimedPuuids = new HashSet<string>(StringComparer.Ordinal);

        // Refresh in save-sized slices, each loading its own accounts and rank snapshots
        // and draining the change tracker after its save (#1229). The whole batch used to
        // be loaded up front and held tracked across two Riot calls per account — up to
        // BatchSize accounts kept alive for the length of hundreds of HTTP round-trips.
        // The loads have to move inside the slice for the drain to be safe: both the
        // accounts and the rank snapshots are mutated in place (RankSnapshotWriter stamps
        // LastRankSyncAtUtc / Score and overwrites the day's snapshot row), and a detached
        // entity would take those writes and persist none of them.
        for (var offset = 0; offset < accounts.Count; offset += saveBatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var slice = accounts.Skip(offset).Take(saveBatchSize).ToList();
            var accountsByKey = await session.RiotAccounts.GetByKeysAsync(slice, ct);
            var latestByAccountId = await session.RankSnapshots.GetLatestForAccountsAsync(
                accountsByKey.Values.Select(account => account.Id).ToList(),
                ct);

            foreach (var account in slice)
            {
                ct.ThrowIfCancellationRequested();
                if (!accountsByKey.TryGetValue(account, out var accountEntity))
                {
                    summary.ProfileFailed++;
                    continue;
                }

                await RefreshSingleAccountAsync(
                    session, accountEntity, latestByAccountId, rankFreshness, nowUtc, claimedPuuids, summary, ct);
            }

            await session.SaveChangesAsync(ct);
            session.ClearTracking();
        }

        return summary;
    }

    private async Task RefreshSingleAccountAsync(
        IDataSession session,
        RiotAccount account,
        IReadOnlyDictionary<Guid, RankSnapshot> latestByAccountId,
        TimeSpan rankFreshness,
        DateTime nowUtc,
        HashSet<string> claimedPuuids,
        RefreshSummary summary,
        CancellationToken ct)
    {
        if (!PlatformId.TryParse(account.PlatformId, out var platform))
        {
            // Stamped, unlike the transient failures below. An unparseable platform_id is
            // a permanent condition — no future run resolves it — and every bucket of
            // GetAccountsForRefreshAsync drains oldest-UpdatedAtUtc-first, so leaving the
            // stamp alone parks the row at the head of every batch and burns a slot that
            // an account we can actually refresh should have had (#1223).
            account.UpdatedAtUtc = nowUtc;
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
            var outcome = await TryRecoverByRiotIdAsync(session, account, region, nowUtc, claimedPuuids, ct);
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
        // and vice versa. Both are flushed by the slice's SaveChangesAsync in
        // RefreshAccountsAsync.

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

            switch (outcome)
            {
                case RankSnapshotOutcome.Inserted:
                    summary.RankInserted++;
                    break;
                case RankSnapshotOutcome.Updated:
                    summary.RankUpdated++;
                    break;
                default:
                    summary.RankUnchanged++;
                    break;
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
    /// <para>
    /// <paramref name="claimedPuuids"/> carries the PUUIDs already handed out earlier in
    /// the same batch: the collision guard below has to consult it as well as the
    /// database, because none of this run's writes are visible to a query until the
    /// batch's single SaveChangesAsync (#1223).
    /// </para>
    /// </summary>
    private async Task<RecoveryOutcome> TryRecoverByRiotIdAsync(
        IDataSession session,
        RiotAccount account,
        RegionalRoute region,
        DateTime nowUtc,
        HashSet<string> claimedPuuids,
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
        // "Another row" means a row in the database *or* an account earlier in this
        // batch that recovered to the same PUUID — the in-flight ones are invisible to
        // ExistsByPuuidAsync until the batch is saved, and two of them slipping through
        // is precisely the unique-index violation this guard exists to prevent (#1223).
        if (!string.Equals(resolved.Puuid, account.Puuid, StringComparison.Ordinal)
            && (claimedPuuids.Contains(resolved.Puuid)
                || await session.RiotAccounts.ExistsByPuuidAsync(resolved.Puuid, ct)))
        {
            logger.LogWarning(
                "Riot account {Platform}/{Puuid} recovered to PUUID {NewPuuid} already held by another row; invalidating the stale duplicate.",
                account.PlatformId,
                account.Puuid,
                resolved.Puuid);
            return RecoveryOutcome.Unrecoverable;
        }

        claimedPuuids.Add(resolved.Puuid);
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

    private static AccountRefreshSummary BuildSuccessPayload(RefreshSummary summary)
    {
        return new AccountRefreshSummary(
            summary.Selected,
            summary.ProfileUpdated,
            summary.ProfileRecovered,
            summary.ProfileInvalidated,
            summary.ProfileSkipped,
            summary.ProfileFailed,
            summary.RankInserted,
            summary.RankUpdated,
            summary.RankUnchanged,
            summary.RankSkippedUnranked,
            summary.RankSkippedFresh,
            summary.RankFailed);
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
        public int RankUpdated { get; set; }
        public int RankUnchanged { get; set; }
        public int RankSkippedUnranked { get; set; }
        public int RankSkippedFresh { get; set; }
        public int RankFailed { get; set; }
    }
}
