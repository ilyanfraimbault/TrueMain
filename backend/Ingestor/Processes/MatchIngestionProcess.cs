using Core;
using Core.Lol.Identifiers;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Common;
using Ingestor.Processes.Components.MatchIngestion;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public sealed class MatchIngestionProcess(
    ILogger<MatchIngestionProcess> logger,
    IDataSessionFactory sessionFactory,
    IMatchClaimService matchClaimService,
    IMatchSnapshotWriter matchSnapshotWriter,
    ITimelineIngestionService timelineIngestionService,
    IAccountValidationService accountValidationService,
    IOptions<MatchIngestionOptions> matchOptions) : IIngestorProcess
{
    public string Name => "MatchIngestion";

    public async Task<object?> RunCoreAsync(CancellationToken ct)
    {
        var options = matchOptions.Value;
        var platforms = PlatformNormalizer.Normalize(options.Platforms);

        if (platforms.Count == 0)
        {
            logger.LogWarning("No platforms configured (MatchIngestion:Platforms).");
            return new { reason = "No platforms configured.", selected = 0 };
        }

        var claimedAccounts = await ClaimAccountsAsync(platforms, options, ct);
        var summary = await IngestClaimedAccountsAsync(claimedAccounts, platforms, options, ct);
        LogPlatformSummaries(summary.ByPlatform);
        return BuildSuccessPayload(summary);
    }

    private async Task<IReadOnlyList<AccountKey>> ClaimAccountsAsync(
        IReadOnlyCollection<string> platforms,
        MatchIngestionOptions options,
        CancellationToken ct)
    {
        var lease = TimeSpan.FromMinutes(Math.Max(1, options.ClaimLeaseMinutes));
        var claimedAccounts = await matchClaimService.ClaimAsync(platforms, options.BatchSize, lease, ct);
        if (claimedAccounts.Count == 0)
        {
            logger.LogInformation("No queued accounts to ingest.");
        }

        return claimedAccounts;
    }

    private async Task<IngestionSummary> IngestClaimedAccountsAsync(
        IReadOnlyList<AccountKey> claimedAccounts,
        IReadOnlyCollection<string> platforms,
        MatchIngestionOptions options,
        CancellationToken ct)
    {
        var summary = new IngestionSummary(platforms);

        foreach (var account in claimedAccounts)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var accountSummary = await IngestSingleAccountAsync(account, options, ct);
                summary.TotalAccounts++;
                summary.TotalInserted += accountSummary.Inserted;
                summary.TotalSkipped += accountSummary.Skipped;
                summary.TotalTimelines += accountSummary.TimelinesUpdated;
                UpdatePlatformSummary(summary.ByPlatform, accountSummary);
            }
            catch (Exception ex)
            {
                summary.TotalErrors++;
                logger.LogError(
                    ex,
                    "Match ingestion failed for {Platform}/{Puuid}. Reverting to queued.",
                    account.PlatformId,
                    account.Puuid);
                await accountValidationService.RevertAsync(account, ct);
            }
        }

        return summary;
    }

    private static void UpdatePlatformSummary(
        IDictionary<string, PlatformSummary> summaryByPlatform,
        AccountIngestionSummary accountSummary)
    {
        if (!summaryByPlatform.TryGetValue(accountSummary.PlatformId, out var platformSummary))
        {
            platformSummary = new PlatformSummary();
            summaryByPlatform[accountSummary.PlatformId] = platformSummary;
        }

        platformSummary.AccountsProcessed++;
        platformSummary.MatchesInserted += accountSummary.Inserted;
        platformSummary.MatchesSkipped += accountSummary.Skipped;
        platformSummary.TimelinesUpdated += accountSummary.TimelinesUpdated;
    }

    private async Task<AccountIngestionSummary> IngestSingleAccountAsync(
        AccountKey account,
        MatchIngestionOptions options,
        CancellationToken ct)
    {
        var platformId = account.PlatformId.ToUpperInvariant();
        if (!PlatformId.TryParse(platformId, out var platform))
        {
            throw new InvalidOperationException($"Unknown platform {platformId}.");
        }

        var region = platform.Route.ToRegional();
        await using var session = await sessionFactory.CreateAsync(ct);

        // Wrap the snapshot, timeline, and catalog writes for this account in a
        // single transaction so a mid-loop crash cannot leave partially ingested
        // matches behind. EF Core automatically creates a savepoint before each
        // SaveChanges while a transaction is in progress, so the catalog upsert's
        // own DbUpdateException recovery still works without poisoning the
        // transaction.
        await using var transaction = await session.BeginTransactionAsync(ct);

        var snapshotResult = await matchSnapshotWriter.IngestSnapshotsAsync(
            session,
            platformId,
            account.Puuid,
            region,
            options.MatchesPerAccount,
            options.SaveBatchSizeMatches,
            options.MaxMatchFetchConcurrency,
            ct);

        var timelineUpdated = await timelineIngestionService.IngestTimelinesAsync(
            session,
            region,
            snapshotResult.AllMatchIds,
            snapshotResult.NewMatchIds,
            options.SaveBatchSizeMatches,
            ct);

        await transaction.CommitAsync(ct);

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

    private void LogPlatformSummaries(IReadOnlyDictionary<string, PlatformSummary> summaryByPlatform)
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

    private static object BuildSuccessPayload(IngestionSummary summary)
    {
        return new
        {
            accountsProcessed = summary.TotalAccounts,
            matchesInserted = summary.TotalInserted,
            matchesSkipped = summary.TotalSkipped,
            timelinesUpdated = summary.TotalTimelines,
            errors = summary.TotalErrors,
            byPlatform = summary.ByPlatform
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
        };
    }

    private sealed record AccountIngestionSummary(string PlatformId, int Inserted, int Skipped, int TimelinesUpdated);

    private sealed class IngestionSummary
    {
        public IngestionSummary(IEnumerable<string> platforms)
        {
            ByPlatform = platforms.ToDictionary(platform => platform, _ => new PlatformSummary());
        }

        public Dictionary<string, PlatformSummary> ByPlatform { get; }
        public int TotalAccounts { get; set; }
        public int TotalInserted { get; set; }
        public int TotalSkipped { get; set; }
        public int TotalTimelines { get; set; }
        public int TotalErrors { get; set; }
    }

    private sealed class PlatformSummary
    {
        public int AccountsProcessed { get; set; }
        public int MatchesInserted { get; set; }
        public int MatchesSkipped { get; set; }
        public int TimelinesUpdated { get; set; }
    }
}
