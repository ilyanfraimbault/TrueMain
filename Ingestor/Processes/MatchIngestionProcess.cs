using Core;
using Data.Entities;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Services;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public sealed class MatchIngestionProcess(
    ILogger<MatchIngestionProcess> logger,
    IDataSessionFactory sessionFactory,
    IProcessRunRecorder runRecorder,
    IMatchClaimService matchClaimService,
    IMatchSnapshotWriter matchSnapshotWriter,
    ITimelineIngestionService timelineIngestionService,
    IAccountValidationService accountValidationService,
    IOptions<MatchIngestionOptions> matchOptions)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        var options = matchOptions.Value;

        if (options.Platforms.Count == 0)
        {
            logger.LogWarning("No platforms configured (MatchIngestion:Platforms).");
            await RecordNoOpAsync(startedAt, "No platforms configured.", ct);
            return;
        }

        var platforms = options.Platforms
            .Where(platform => !string.IsNullOrWhiteSpace(platform))
            .Select(platform => platform.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var summaryByPlatform = platforms.ToDictionary(platform => platform, _ => new PlatformSummary());
        var lease = TimeSpan.FromMinutes(Math.Max(1, options.ClaimLeaseMinutes));

        var totalAccounts = 0;
        var totalInserted = 0;
        var totalSkipped = 0;
        var totalTimelines = 0;
        var totalErrors = 0;

        try
        {
            var claimedAccounts = await matchClaimService.ClaimAsync(platforms, options.BatchSize, lease, ct);
            if (claimedAccounts.Count == 0)
            {
                logger.LogInformation("No queued accounts to ingest.");
            }

            foreach (var account in claimedAccounts)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var accountSummary = await ProcessAccountAsync(account, options, ct);
                    if (!summaryByPlatform.TryGetValue(accountSummary.PlatformId, out var platformSummary))
                    {
                        platformSummary = new PlatformSummary();
                        summaryByPlatform[accountSummary.PlatformId] = platformSummary;
                    }

                    platformSummary.AccountsProcessed++;
                    platformSummary.MatchesInserted += accountSummary.Inserted;
                    platformSummary.MatchesSkipped += accountSummary.Skipped;
                    platformSummary.TimelinesUpdated += accountSummary.TimelinesUpdated;

                    totalAccounts++;
                    totalInserted += accountSummary.Inserted;
                    totalSkipped += accountSummary.Skipped;
                    totalTimelines += accountSummary.TimelinesUpdated;
                }
                catch (Exception ex)
                {
                    totalErrors++;
                    logger.LogError(
                        ex,
                        "Match ingestion failed for {Platform}/{Puuid}. Reverting to queued.",
                        account.PlatformId,
                        account.Puuid);
                    await accountValidationService.RevertAsync(account, ct);
                }
            }

            LogSummaryByPlatform(summaryByPlatform);

            await runRecorder.RecordAsync(
                "MatchIngestion",
                startedAt,
                DateTime.UtcNow,
                ProcessRunStatus.Success,
                new
                {
                    accountsProcessed = totalAccounts,
                    matchesInserted = totalInserted,
                    matchesSkipped = totalSkipped,
                    timelinesUpdated = totalTimelines,
                    errors = totalErrors,
                    byPlatform = summaryByPlatform
                        .Where(entry => entry.Value.AccountsProcessed > 0)
                        .Select(entry => new
                        {
                            platform = entry.Key,
                            accountsProcessed = entry.Value.AccountsProcessed,
                            matchesInserted = entry.Value.MatchesInserted,
                            matchesSkipped = entry.Value.MatchesSkipped,
                            timelinesUpdated = entry.Value.TimelinesUpdated
                        })
                        .ToList()
                },
                null,
                ct);
        }
        catch (Exception ex)
        {
            await runRecorder.RecordAsync(
                "MatchIngestion",
                startedAt,
                DateTime.UtcNow,
                ProcessRunStatus.Failed,
                null,
                ex.Message,
                ct);
            throw;
        }
    }

    private async Task<AccountIngestionSummary> ProcessAccountAsync(
        AccountKey account,
        MatchIngestionOptions options,
        CancellationToken ct)
    {
        var platformId = account.PlatformId.ToUpperInvariant();
        if (!RiotDataHelpers.TryParsePlatform(platformId, out var platform))
        {
            throw new InvalidOperationException($"Unknown platform {platformId}.");
        }

        var region = RiotRouting.FromPlatform(platform);
        await using var session = await sessionFactory.CreateAsync(ct);

        var snapshotResult = await matchSnapshotWriter.IngestSnapshotsAsync(
            session,
            platformId,
            account.Puuid,
            region,
            options.MatchesPerAccount,
            options.SaveBatchSizeMatches,
            ct);

        var timelineUpdated = await timelineIngestionService.IngestTimelinesAsync(
            session,
            region,
            snapshotResult.AllMatchIds,
            snapshotResult.NewMatchIds,
            options.SaveBatchSizeMatches,
            ct);

        await accountValidationService.ValidateAsync(account, ct);

        logger.LogInformation(
            "Match ingestion for {Platform}/{Puuid}: inserted={Inserted}, skipped={Skipped}, timelinesUpdated={Timelines}.",
            platformId,
            account.Puuid,
            snapshotResult.Inserted,
            snapshotResult.Skipped,
            timelineUpdated);

        return new AccountIngestionSummary(platformId, snapshotResult.Inserted, snapshotResult.Skipped, timelineUpdated);
    }

    private void LogSummaryByPlatform(IReadOnlyDictionary<string, PlatformSummary> summaryByPlatform)
    {
        foreach (var (platformId, summary) in summaryByPlatform)
        {
            if (summary.AccountsProcessed == 0)
            {
                continue;
            }

            logger.LogInformation(
                "Match ingestion summary for {Platform}: accounts={Accounts}, matchesInserted={Inserted}, matchesSkipped={Skipped}, timelinesUpdated={Timelines}.",
                platformId,
                summary.AccountsProcessed,
                summary.MatchesInserted,
                summary.MatchesSkipped,
                summary.TimelinesUpdated);
        }
    }

    private async Task RecordNoOpAsync(DateTime startedAtUtc, string reason, CancellationToken ct)
    {
        await runRecorder.RecordAsync(
            "MatchIngestion",
            startedAtUtc,
            DateTime.UtcNow,
            ProcessRunStatus.Success,
            new { reason, selected = 0 },
            null,
            ct);
    }

    private sealed record AccountIngestionSummary(string PlatformId, int Inserted, int Skipped, int TimelinesUpdated);

    private sealed class PlatformSummary
    {
        public int AccountsProcessed { get; set; }
        public int MatchesInserted { get; set; }
        public int MatchesSkipped { get; set; }
        public int TimelinesUpdated { get; set; }
    }
}
