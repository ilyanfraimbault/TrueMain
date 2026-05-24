using Core;
using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Ranking;
using Ingestor.Riot;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public sealed class AccountRefreshProcess(
    ILogger<AccountRefreshProcess> logger,
    IRiotAccountClient riotAccountClient,
    IRiotPlatformClient riotPlatformClient,
    IDataSessionFactory sessionFactory,
    IRankSnapshotWriter rankSnapshotWriter,
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
            "Account refresh summary: selected={Selected}, profileUpdated={ProfileUpdated}, profileSkipped={ProfileSkipped}, profileFailed={ProfileFailed}, rankInserted={RankInserted}, rankUnchanged={RankUnchanged}, rankSkippedUnranked={RankSkippedUnranked}, rankSkippedFresh={RankSkippedFresh}, rankFailed={RankFailed}.",
            summary.Selected,
            summary.ProfileUpdated,
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
        var nowUtc = DateTime.UtcNow;
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
        if (!RiotDataHelpers.TryParsePlatform(account.PlatformId, out var platform))
        {
            logger.LogWarning(
                "Skipping riot account {Puuid}: invalid platform {PlatformId}.",
                account.Puuid,
                account.PlatformId);
            summary.ProfileSkipped++;
            return;
        }

        try
        {
            var region = RiotRouting.FromPlatform(platform);
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
            var outcome = rankSnapshotWriter.Write(
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

    private static object BuildSuccessPayload(RefreshSummary summary)
    {
        return new
        {
            selected = summary.Selected,
            profileUpdated = summary.ProfileUpdated,
            profileSkipped = summary.ProfileSkipped,
            profileFailed = summary.ProfileFailed,
            rankInserted = summary.RankInserted,
            rankUnchanged = summary.RankUnchanged,
            rankSkippedUnranked = summary.RankSkippedUnranked,
            rankSkippedFresh = summary.RankSkippedFresh,
            rankFailed = summary.RankFailed
        };
    }

    private sealed class RefreshSummary
    {
        public int Selected { get; set; }
        public int ProfileUpdated { get; set; }
        public int ProfileSkipped { get; set; }
        public int ProfileFailed { get; set; }
        public int RankInserted { get; set; }
        public int RankUnchanged { get; set; }
        public int RankSkippedUnranked { get; set; }
        public int RankSkippedFresh { get; set; }
        public int RankFailed { get; set; }
    }
}
