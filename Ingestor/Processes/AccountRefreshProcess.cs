using Core;
using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Riot;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public class AccountRefreshProcess(
    ILogger<AccountRefreshProcess> logger,
    IRiotAccountClient riotAccountClient,
    IDataSessionFactory sessionFactory,
    IOptions<AccountRefreshOptions> refreshOptions) : IIngestorProcess
{
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
            "Account refresh summary: selected={Selected}, updated={Updated}, skipped={Skipped}, failed={Failed}.",
            summary.Selected,
            summary.Updated,
            summary.Skipped,
            summary.Failed);

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

        foreach (var account in accounts)
        {
            ct.ThrowIfCancellationRequested();
            if (!accountsByKey.TryGetValue(account, out var accountEntity))
            {
                summary.Failed++;
                continue;
            }

            var outcome = await RefreshSingleAccountAsync(accountEntity, nowUtc, ct);
            summary.Updated += outcome.Updated;
            summary.Skipped += outcome.Skipped;
            summary.Failed += outcome.Failed;
        }

        await session.SaveChangesAsync(ct);
        return summary;
    }

    private async Task<RefreshOutcome> RefreshSingleAccountAsync(
        RiotAccount account,
        DateTime nowUtc,
        CancellationToken ct)
    {
        if (!RiotDataHelpers.TryParsePlatform(account.PlatformId, out var platform))
        {
            logger.LogWarning(
                "Skipping riot account {Puuid}: invalid platform {PlatformId}.",
                account.Puuid,
                account.PlatformId);
            return RefreshOutcome.SkippedAccount;
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
            return RefreshOutcome.UpdatedAccount;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to refresh riot account {Platform}/{Puuid}.",
                account.PlatformId,
                account.Puuid);
            return RefreshOutcome.FailedAccount;
        }
    }

    private static object BuildSuccessPayload(RefreshSummary summary)
    {
        return new
        {
            selected = summary.Selected,
            updated = summary.Updated,
            skipped = summary.Skipped,
            failed = summary.Failed
        };
    }

    private sealed class RefreshSummary
    {
        public int Selected { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
    }

    private sealed record RefreshOutcome(int Updated, int Skipped, int Failed)
    {
        public static RefreshOutcome UpdatedAccount => new(1, 0, 0);
        public static RefreshOutcome SkippedAccount => new(0, 1, 0);
        public static RefreshOutcome FailedAccount => new(0, 0, 1);
    }
}
