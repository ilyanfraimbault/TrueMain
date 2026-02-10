using Core;
using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Riot;
using Ingestor.Services;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public class AccountRefreshProcess(
    ILogger<AccountRefreshProcess> logger,
    IRiotAccountClient riotAccountClient,
    IDataSessionFactory sessionFactory,
    IProcessRunRecorder runRecorder,
    IOptions<AccountRefreshOptions> refreshOptions)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var options = refreshOptions.Value;
        var batchSize = Math.Max(1, options.BatchSize);
        var startedAt = DateTime.UtcNow;

        try
        {
            await using var session = await sessionFactory.CreateAsync(ct);

            var accounts = await session.RiotAccounts.GetAccountsForRefreshAsync(batchSize, ct);

            if (accounts.Count == 0)
            {
                logger.LogInformation("No riot accounts found for refresh.");
                var finishedAtNoOp = DateTime.UtcNow;
                await runRecorder.RecordAsync(
                    "AccountRefresh",
                    startedAt,
                    finishedAtNoOp,
                    ProcessRunStatus.Success,
                    new { reason = "No riot accounts found for refresh.", selected = 0 },
                    null,
                    ct);
                return;
            }

            var nowUtc = DateTime.UtcNow;
            var updated = 0;
            var skipped = 0;
            var failed = 0;

            foreach (var account in accounts)
            {
                ct.ThrowIfCancellationRequested();

                if (!RiotDataHelpers.TryParsePlatform(account.PlatformId, out var platform))
                {
                    skipped++;
                    logger.LogWarning(
                        "Skipping riot account {Puuid}: invalid platform {PlatformId}.",
                        account.Puuid,
                        account.PlatformId);
                    continue;
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
                    updated++;
                }
                catch (Exception ex)
                {
                    failed++;
                    logger.LogWarning(
                        ex,
                        "Failed to refresh riot account {Platform}/{Puuid}.",
                        account.PlatformId,
                        account.Puuid);
                }
            }

            await session.SaveChangesAsync(ct);

            logger.LogInformation(
                "Account refresh summary: selected={Selected}, updated={Updated}, skipped={Skipped}, failed={Failed}.",
                accounts.Count,
                updated,
                skipped,
                failed);

            var finishedAt = DateTime.UtcNow;
            await runRecorder.RecordAsync("AccountRefresh", startedAt, finishedAt, ProcessRunStatus.Success,
                new { selected = accounts.Count, updated, skipped, failed }, null, ct);
        }
        catch (Exception ex)
        {
            var finishedAt = DateTime.UtcNow;
            await runRecorder.RecordAsync("AccountRefresh", startedAt, finishedAt, ProcessRunStatus.Failed, null, ex.Message, ct);
            throw;
        }
    }

}
