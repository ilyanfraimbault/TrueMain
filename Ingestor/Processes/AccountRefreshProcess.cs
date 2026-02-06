using Core;
using Data;
using Ingestor.Options;
using Ingestor.Riot;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public class AccountRefreshProcess(
    ILogger<AccountRefreshProcess> logger,
    IRiotAccountClient riotAccountClient,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    IOptions<AccountRefreshOptions> refreshOptions)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var options = refreshOptions.Value;
        var batchSize = Math.Max(1, options.BatchSize);

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var accounts = await db.RiotAccounts
            .OrderBy(a =>
                (a.GameName == null || a.GameName == string.Empty ||
                 a.TagLine == null || a.TagLine == string.Empty)
                    ? 0
                    : 1)
            .ThenBy(a => a.UpdatedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct);

        if (accounts.Count == 0)
        {
            logger.LogInformation("No riot accounts found for refresh.");
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var updated = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var account in accounts)
        {
            ct.ThrowIfCancellationRequested();

            if (!TryParsePlatform(account.PlatformId, out var platform))
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

        LogPendingChanges(logger, db, "AccountRefresh");
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Account refresh summary: selected={Selected}, updated={Updated}, skipped={Skipped}, failed={Failed}.",
            accounts.Count,
            updated,
            skipped,
            failed);
    }

    private static bool TryParsePlatform(string platform, out PlatformRoute route)
        => Enum.TryParse(platform.Trim(), ignoreCase: true, out route);

    private static void LogPendingChanges(ILogger logger, TrueMainDbContext db, string stage)
    {
        var added = 0;
        var modified = 0;
        var deleted = 0;

        foreach (var entry in db.ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    added++;
                    break;
                case EntityState.Modified:
                    modified++;
                    break;
                case EntityState.Deleted:
                    deleted++;
                    break;
            }
        }

        if (added == 0 && modified == 0 && deleted == 0)
        {
            return;
        }

        logger.LogDebug(
            "{Stage} DB changes: added={Added}, modified={Modified}, deleted={Deleted}.",
            stage,
            added,
            modified,
            deleted);
    }
}
