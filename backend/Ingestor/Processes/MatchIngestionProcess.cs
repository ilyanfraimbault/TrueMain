using System.Collections.Concurrent;
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

        var lease = TimeSpan.FromMinutes(Math.Max(1, options.ClaimLeaseMinutes));

        // Before claiming, not after: what a dead run left behind becomes claimable in this
        // same pass instead of waiting a full cycle (#1344).
        var released = await matchClaimService.ReleaseExpiredClaimsAsync(lease, ct);

        var claimedAccounts = await ClaimAccountsAsync(platforms, options, lease, ct);
        var summary = await IngestClaimedAccountsAsync(claimedAccounts, platforms, options, ct);
        LogPlatformSummaries(summary.ByPlatform);
        return BuildSuccessPayload(summary, released);
    }

    private async Task<IReadOnlyList<AccountKey>> ClaimAccountsAsync(
        IReadOnlyCollection<string> platforms,
        MatchIngestionOptions options,
        TimeSpan lease,
        CancellationToken ct)
    {
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

        // One worker per platform, each still sequential over its own accounts (#1359).
        // Riot meters its application limit per routing value, so a single serial loop
        // spent the whole run inside one region's allowance while the others idled —
        // measured at 0.77 req/s on production, which is exactly one region's sustained
        // cap. The parallelism that pays is therefore *across* routing values, not inside
        // one: going wider within a platform would only queue up behind the rate limiter
        // that now governs it, while adding contention on the same claim rows.
        //
        // Every collaborator this fans out to is stateless or creates its own DbContext
        // per account (IngestSingleAccountAsync opens the session), so the only shared
        // mutable state is the per-worker result merged below.
        var platformGroups = claimedAccounts
            .GroupBy(account => account.PlatformId.ToUpperInvariant(), StringComparer.Ordinal)
            .ToList();

        var results = new ConcurrentBag<PlatformIngestionResult>();
        await Parallel.ForEachAsync(
            platformGroups,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, platformGroups.Count),
                CancellationToken = ct
            },
            async (group, token) => results.Add(await IngestPlatformAsync(group.Key, [.. group], options, token)));

        // Merged in a deterministic order: the bag's enumeration order is not, and the
        // run summary is read by humans comparing one cycle to the next.
        foreach (var result in results.OrderBy(result => result.PlatformId, StringComparer.Ordinal))
        {
            summary.TotalAccounts += result.Accounts;
            summary.TotalValidated += result.Validated;
            summary.TotalInserted += result.Inserted;
            summary.TotalSkipped += result.Skipped;
            summary.TotalSkippedWrongQueue += result.SkippedWrongQueue;
            summary.TotalTimelines += result.TimelinesUpdated;
            summary.TotalErrors += result.Errors;
            summary.TotalWithoutNewMatches += result.WithoutNewMatches;
            summary.ByPlatform[result.PlatformId] = result.Platform;
        }

        return summary;
    }

    /// <summary>
    /// Ingests one platform's claimed accounts, one after another. A failure is recorded
    /// against this platform and the loop moves on, exactly as the single serial loop did:
    /// one bad account must not cost the rest of its own platform, and it must not cost
    /// the other platforms either, which is why the result is local and merged afterwards.
    /// </summary>
    private async Task<PlatformIngestionResult> IngestPlatformAsync(
        string platformKey,
        IReadOnlyList<AccountKey> claimedAccounts,
        MatchIngestionOptions options,
        CancellationToken ct)
    {
        var result = new PlatformIngestionResult(platformKey);

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
                    result.Errors++;
                    logger.LogError(
                        "Unknown platform {Platform} on claimed account {Puuid}; releasing the claim without ingesting.",
                        account.PlatformId,
                        account.Puuid);
                    await accountValidationService.ReleaseUningestableAsync(account, ct);
                    continue;
                }

                var accountSummary = await IngestSingleAccountAsync(account, platformId, platform, options, ct);
                result.Record(accountSummary);
            }
            catch (Exception ex)
            {
                result.Errors++;
                logger.LogError(
                    ex,
                    "Match ingestion failed for {Platform}/{Puuid}. Reverting to queued.",
                    account.PlatformId,
                    account.Puuid);
                await RevertClaimAsync(account, ex, ct);
            }
        }

        return result;
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
            "Match ingestion for {Platform}/{Puuid}: inserted={Inserted}, skipped={Skipped}, skippedWrongQueue={SkippedWrongQueue}, timelinesUpdated={Timelines}.",
            platformId,
            account.Puuid,
            snapshotResult.Inserted,
            snapshotResult.Skipped,
            snapshotResult.SkippedWrongQueue,
            timelineUpdated);

        return new AccountIngestionSummary(
            platformId,
            snapshotResult.Inserted,
            snapshotResult.Skipped,
            snapshotResult.SkippedWrongQueue,
            timelineUpdated,
            validated);
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
                "Match ingestion summary for {Platform}: accounts={Accounts}, matchesInserted={Inserted}, matchesSkipped={Skipped}, matchesSkippedWrongQueue={SkippedWrongQueue}, timelinesUpdated={Timelines}.",
                platformId,
                summary.AccountsProcessed,
                summary.MatchesInserted,
                summary.MatchesSkipped,
                summary.MatchesSkippedWrongQueue,
                summary.TimelinesUpdated);
        }
    }

    private static MatchIngestionSummary BuildSuccessPayload(IngestionSummary summary, ExpiredClaimRelease released)
    {
        return new MatchIngestionSummary(
            summary.TotalAccounts,
            summary.TotalInserted,
            summary.TotalSkipped,
            summary.TotalTimelines,
            summary.TotalErrors,
            summary.TotalValidated,
            released.Candidates,
            released.Accounts,
            summary.TotalSkippedWrongQueue,
            summary.ByPlatform
                .Where(entry => entry.Value.AccountsProcessed > 0)
                .Select(entry => new MatchIngestionPlatformSummary(
                    entry.Key,
                    entry.Value.AccountsProcessed,
                    entry.Value.MatchesInserted,
                    entry.Value.MatchesSkipped,
                    entry.Value.TimelinesUpdated,
                    entry.Value.MatchesSkippedWrongQueue))
                .ToList(),
            summary.TotalWithoutNewMatches);
    }

    /// <summary>
    /// One platform worker's tally. Local to the worker, so the platforms can run
    /// concurrently without sharing a counter, and merged into the run summary once they
    /// have all finished.
    /// </summary>
    private sealed class PlatformIngestionResult(string platformId)
    {
        public string PlatformId { get; } = platformId;

        public PlatformSummary Platform { get; } = new();

        public int Accounts { get; private set; }

        public int Validated { get; private set; }

        public int Inserted { get; private set; }

        public int Skipped { get; private set; }

        public int SkippedWrongQueue { get; private set; }

        public int TimelinesUpdated { get; private set; }

        public int Errors { get; set; }

        public int WithoutNewMatches { get; private set; }

        public void Record(AccountIngestionSummary accountSummary)
        {
            Accounts++;
            if (accountSummary.Inserted == 0)
            {
                // A visit that stored nothing: the player had not played since the last one.
                // #1360's claim ordering exists to make this rare, so it is counted rather
                // than inferred from the difference between two other numbers.
                WithoutNewMatches++;
            }

            if (accountSummary.Validated)
            {
                Validated++;
            }

            Inserted += accountSummary.Inserted;
            Skipped += accountSummary.Skipped;
            SkippedWrongQueue += accountSummary.SkippedWrongQueue;
            TimelinesUpdated += accountSummary.TimelinesUpdated;

            Platform.AccountsProcessed++;
            Platform.MatchesInserted += accountSummary.Inserted;
            Platform.MatchesSkipped += accountSummary.Skipped;
            Platform.MatchesSkippedWrongQueue += accountSummary.SkippedWrongQueue;
            Platform.TimelinesUpdated += accountSummary.TimelinesUpdated;
        }
    }

    private sealed record AccountIngestionSummary(
        string PlatformId,
        int Inserted,
        int Skipped,
        int SkippedWrongQueue,
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
        public int TotalSkippedWrongQueue { get; set; }
        public int TotalTimelines { get; set; }
        public int TotalErrors { get; set; }
        public int TotalWithoutNewMatches { get; set; }
    }

    private sealed class PlatformSummary
    {
        public int AccountsProcessed { get; set; }
        public int MatchesInserted { get; set; }
        public int MatchesSkipped { get; set; }
        public int MatchesSkippedWrongQueue { get; set; }
        public int TimelinesUpdated { get; set; }
    }
}
