using Core;
using Core.Lol.Identifiers;
using Data.Logging;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Common;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Processes.Summaries;
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

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var options = matchOptions.Value;
        var platforms = PlatformNormalizer.Normalize(options.Platforms);

        if (platforms.Count == 0)
        {
            logger.LogWarning("No platforms configured (MatchIngestion:Platforms).");
            return new NoWorkSummary("No platforms configured.", 0);
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
        var claimedAccounts = await matchClaimService.ClaimAsync(
            platforms,
            options.BatchSize,
            options.EstablishedMainShare,
            lease,
            ct);
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
                var platformId = account.PlatformId.ToUpperInvariant();
                if (!PlatformId.TryParse(platformId, out var platform))
                {
                    // A state, not an exception: an account row carrying a platform_id
                    // that no longer parses is an expected data condition, not a fault.
                    // Throwing routed it through the catch below and RevertAsync, which
                    // leaves LastMatchIngestAtUtc untouched — so the row came straight
                    // back at the head of the next claim and consumed one of the batch's
                    // slots on every single cycle (#1223). Still inside the try: a failure
                    // of the release itself must not abort the rest of the batch.
                    summary.TotalErrors++;
                    logger.LogError(
                        "Unknown platform {Platform} on claimed account {Puuid}; releasing the claim without ingesting.",
                        account.PlatformId,
                        account.Puuid);
                    await accountValidationService.ReleaseUningestableAsync(account, ct);
                    continue;
                }

                var accountSummary = await IngestSingleAccountAsync(account, platformId, platform, options, ct);
                summary.TotalAccounts++;
                if (accountSummary.Validated)
                {
                    summary.TotalValidated++;
                }

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
                await RevertClaimAsync(account, ex, ct);
            }
        }

        return summary;
    }

    private async Task RevertClaimAsync(AccountKey account, Exception ingestionException, CancellationToken ct)
    {
        try
        {
            await accountValidationService.RevertAsync(account, ct);
        }
        catch (OperationCanceledException)
        {
            // A cancellation is a cooperative shutdown signal, not a revert failure.
            // Let it propagate so the loop stops instead of logging it for every
            // remaining account and swallowing the shutdown.
            throw;
        }
        catch (Exception revertException)
        {
            // The revert that should return the claim to Queued failed too. Swallowing
            // it here keeps the batch going for the remaining accounts, but the claim
            // now stays Processing until its lease expires with no other signal, so we
            // surface it as a named ops event (#263) carrying both the revert failure
            // and the original ingestion failure as its cause.
            logger.LogError(
                OpsEvents.MatchRevertFailed,
                new AggregateException(revertException, ingestionException),
                "Failed to revert claim for {Platform}/{Puuid} after ingestion error; "
                + "candidates remain Processing until the claim lease expires.",
                account.PlatformId,
                account.Puuid);
        }
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
        string platformId,
        PlatformId platform,
        MatchIngestionOptions options,
        CancellationToken ct)
    {
        var region = platform.Route.ToRegional();
        await using var session = await sessionFactory.CreateAsync(ct);

        // Phase 1 — fetch. Up to 20 match-v5 calls and 20 match-v5 timeline calls, each
        // able to burn the client's whole EffectiveTotalRequestTimeout under a 429
        // backoff. Deliberately outside the transaction (#264, #1229): running it under
        // BEGIN left the connection `idle in transaction` for minutes per account,
        // holding the claim locks and pinning VACUUM's horizon for reads that take no
        // locks at all. The payloads are bounded by MatchesPerAccount, so materialising
        // them costs a few MB per account.
        var snapshotPlan = await matchSnapshotWriter.PrepareAsync(
            session,
            platformId,
            account.Puuid,
            region,
            options.MatchesPerAccount,
            options.MaxMatchFetchConcurrency,
            ct);

        var timelinePlan = await timelineIngestionService.PrepareAsync(
            session,
            region,
            snapshotPlan.AllMatchIds,
            snapshotPlan.TargetMatches.Select(match => match.MatchId).ToList(),
            ct);

        // Phase 2 — write. The transaction now spans the writes and nothing else, and
        // still delivers the property it was opened for: a crash mid-loop cannot leave
        // partially ingested matches behind, because every snapshot, timeline and
        // catalog write for the account commits or rolls back as one. EF Core creates a
        // savepoint before each SaveChanges while a transaction is in progress, so the
        // catalog upsert's own DbUpdateException recovery still works without poisoning
        // it. A crash during phase 1 writes nothing at all, and the account is reverted
        // to Queued (or ages out of its claim lease) and re-fetched from scratch —
        // GetExistingMatchIdsAsync and the TimelineIngested flag make that replay
        // idempotent, exactly as before.
        await using var transaction = await session.BeginTransactionAsync(ct);

        var snapshotResult = await matchSnapshotWriter.WriteAsync(
            session,
            snapshotPlan,
            platformId,
            account.Puuid,
            options.SaveBatchSizeMatches,
            ct);

        var timelineUpdated = await timelineIngestionService.WriteAsync(
            session,
            timelinePlan,
            options.SaveBatchSizeMatches,
            ct);

        await transaction.CommitAsync(ct);

        var validated = await accountValidationService.ValidateAsync(account, ct);

        logger.LogInformation(
            "Match ingestion for {Platform}/{Puuid}: inserted={Inserted}, skipped={Skipped}, timelinesUpdated={Timelines}.",
            platformId,
            account.Puuid,
            snapshotResult.Inserted,
            snapshotResult.Skipped,
            timelineUpdated);

        return new AccountIngestionSummary(platformId, snapshotResult.Inserted, snapshotResult.Skipped, timelineUpdated, validated);
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

    private static MatchIngestionSummary BuildSuccessPayload(IngestionSummary summary)
    {
        return new MatchIngestionSummary(
            summary.TotalAccounts,
            summary.TotalInserted,
            summary.TotalSkipped,
            summary.TotalTimelines,
            summary.TotalErrors,
            summary.TotalValidated,
            summary.ByPlatform
                .Where(entry => entry.Value.AccountsProcessed > 0)
                .Select(entry => new MatchIngestionPlatformSummary(
                    entry.Key,
                    entry.Value.AccountsProcessed,
                    entry.Value.MatchesInserted,
                    entry.Value.MatchesSkipped,
                    entry.Value.TimelinesUpdated))
                .ToList());
    }

    private sealed record AccountIngestionSummary(
        string PlatformId,
        int Inserted,
        int Skipped,
        int TimelinesUpdated,
        bool Validated);

    private sealed class IngestionSummary
    {
        public IngestionSummary(IEnumerable<string> platforms)
        {
            ByPlatform = platforms.ToDictionary(platform => platform, _ => new PlatformSummary());
        }

        public Dictionary<string, PlatformSummary> ByPlatform { get; }
        public int TotalAccounts { get; set; }
        public int TotalValidated { get; set; }
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
