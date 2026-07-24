using Core.Lol.Identifiers;
using Data.Entities;
using Data.Logging;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Common;
using Ingestor.Processes.Components.Discovery;
using Ingestor.Ranking;
using Ingestor.Riot;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public sealed class DiscoveryProcess(
    ILogger<DiscoveryProcess> logger,
    IRiotPlatformClient riotPlatformClient,
    IDataSessionFactory sessionFactory,
    ILadderDiscoveryService ladderDiscoveryService,
    IAccountUpsertService accountUpsertService,
    ICandidateUpsertService candidateUpsertService,
    IRankSnapshotWriter rankSnapshotWriter,
    TimeProvider timeProvider,
    IOptions<DiscoveryOptions> discoveryOptions) : IIngestorProcess
{
    public string Name => "Discovery";

    public async Task<object?> RunCoreAsync(CancellationToken ct)
    {
        var options = discoveryOptions.Value;
        var platforms = PlatformNormalizer.Normalize(options.Platforms);

        if (platforms.Count == 0)
        {
            logger.LogWarning("No platforms configured (Discovery:Platforms).");
            return new { reason = "No platforms configured.", selected = 0 };
        }

        // Reduced cadence (#487): now that the participant harvest is the primary candidate
        // source, the mastery-backed ladder crawl runs on a maintenance cadence so its Riot
        // budget can be reallocated to match ingestion. Skip when the last completed run is
        // within MinRunInterval. The current run is recorded as Running by the recorder, so
        // GetLastCompletedRunStartAsync excludes it.
        if (options.MinRunInterval > TimeSpan.Zero && await ShouldSkipForCadenceAsync(options.MinRunInterval, ct) is { } lastRunUtc)
        {
            logger.LogInformation(
                "Discovery skipped: last run {LastRunUtc:o} is within MinRunInterval {Interval}.",
                lastRunUtc,
                options.MinRunInterval);
            return new { reason = "Within MinRunInterval; discovery skipped this iteration.", skipped = true };
        }

        var summaries = await DiscoverAcrossPlatformsAsync(platforms, options, ct);
        return BuildSuccessPayload(summaries);
    }

    /// <summary>
    /// Returns the last completed Discovery run time when it is within
    /// <paramref name="minRunInterval"/> (so this iteration should skip), otherwise null.
    /// </summary>
    private async Task<DateTime?> ShouldSkipForCadenceAsync(TimeSpan minRunInterval, CancellationToken ct)
    {
        await using var session = await sessionFactory.CreateAsync(ct);
        var lastRunUtc = await session.ProcessRuns.GetLastCompletedRunStartAsync(Name, ct);
        return lastRunUtc is not null && timeProvider.GetUtcNow().UtcDateTime - lastRunUtc.Value < minRunInterval
            ? lastRunUtc
            : null;
    }

    private async Task<List<PlatformSummary>> DiscoverAcrossPlatformsAsync(
        IReadOnlyCollection<string> platforms,
        DiscoveryOptions options,
        CancellationToken ct)
    {
        var summaries = new List<PlatformSummary>();
        var failures = new List<Exception>();

        foreach (var platformString in platforms)
        {
            ct.ThrowIfCancellationRequested();
            if (!PlatformId.TryParse(platformString, out var platform))
            {
                logger.LogWarning("Skipping unknown platform '{Platform}'.", platformString);
                continue;
            }

            try
            {
                var platformSummary = await ProcessPlatformAsync(platform.Route, options, ct);
                summaries.Add(platformSummary);
                LogPlatformSummary(platformSummary);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One platform hitting a wall (e.g. EUW1 ladder paging stalled behind a
                // Riot 429 backoff) must not abort discovery for the remaining platforms.
                logger.LogError(
                    ex,
                    "Discovery failed for platform {Platform}; continuing with the remaining platforms.",
                    platform.Route);
                failures.Add(ex);
                summaries.Add(new PlatformSummary(platform.Route.ToString()) { FailureReason = ex.Message });
            }
        }

        if (summaries.Count > 0 && failures.Count == summaries.Count)
        {
            // Nothing was discovered anywhere; surface the failure so the run is
            // recorded as Failed instead of masquerading as an empty success. The
            // Count > 0 guard keeps the all-entries-unparseable case from throwing
            // on 0 == 0: a platform string that fails TryParse is skipped without
            // a summary, and Discovery:Platforms is validated non-empty at startup,
            // so an empty list here only ever means "nothing was attempted".
            throw new AggregateException(
                $"Discovery failed for all {summaries.Count} platform(s): "
                + $"{string.Join(", ", summaries.Select(summary => summary.PlatformId))}.",
                failures);
        }

        return summaries;
    }

    private async Task<PlatformSummary> ProcessPlatformAsync(
        PlatformRoute platform,
        DiscoveryOptions options,
        CancellationToken ct)
    {
        var platformId = platform.ToString();
        var summary = new PlatformSummary(platformId);

        await using var session = await sessionFactory.CreateAsync(ct);

        // Sliding window (#486): resume from the persisted per-platform offset so
        // successive runs sweep the whole ladder instead of re-scanning the top.
        var offset = options.SlidingWindowEnabled
            ? await session.DiscoveryCursors.GetOffsetAsync(platformId, ct) ?? 0
            : 0;

        var result = await ladderDiscoveryService.DiscoverSummonersAsync(platform, options, offset, ct);
        var discovered = result.Discovered;

        // Advance the cursor past this window for the next run, wrapping at the ladder
        // end. Written immediately by its own upsert statement, independently of the
        // SaveChanges calls below.
        if (options.SlidingWindowEnabled && result.LadderSize > 0)
        {
            var window = Math.Max(1, options.MaxAccountsPerPlatformPerRun);
            var nextOffset = result.AppliedOffset + window;
            if (nextOffset >= result.LadderSize)
            {
                nextOffset = 0;
            }

            await session.DiscoveryCursors.UpsertOffsetAsync(platformId, nextOffset, timeProvider.GetUtcNow().UtcDateTime, ct);
        }

        if (discovered.Count == 0)
        {
            logger.LogInformation("No ladder entries for platform {Platform}.", platformId);
            // The cursor advance is already persisted by the upsert above, so an empty
            // window still moves the next run forward rather than re-scanning the same
            // slice — and nothing else is staged on this session yet.
            return summary;
        }

        var saveBatchSize = Math.Max(1, options.SaveBatchSize);
        var newAccountsTarget = Math.Max(0, options.NewAccountsTarget);

        var latestByAccountId = await PreloadLatestSnapshotsAsync(session, platformId, discovered, ct);

        var pendingChanges = 0;
        var discoveredAccounts = 0;

        foreach (var item in discovered)
        {
            ct.ThrowIfCancellationRequested();

            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            var upsertResult = await accountUpsertService.UpsertAsync(session, platform, item.Summoner, nowUtc, ct);
            if (upsertResult.IsNew)
            {
                discoveredAccounts++;
                summary.NewAccountsDiscovered++;
            }

            if (item.Rank is not null)
            {
                latestByAccountId.TryGetValue(upsertResult.Account.Id, out var latest);
                var outcome = rankSnapshotWriter.Ingest(session, upsertResult.Account, item.Rank, latest, nowUtc);
                if (outcome == RankSnapshotOutcome.Inserted)
                {
                    summary.RankSnapshotsInserted++;
                }
                else
                {
                    summary.RankSnapshotsUnchanged++;
                }
            }

            var masteries = await riotPlatformClient.GetChampionMasteriesAsync(platform, item.Summoner.Puuid, ct);
            var candidateResult = await candidateUpsertService.UpsertAsync(
                session,
                platformId,
                item.Summoner.Puuid,
                masteries,
                options,
                nowUtc,
                ct);

            summary.AccountsProcessed++;
            summary.CandidatesInserted += candidateResult.Inserted;
            summary.CandidatesUpdated += candidateResult.Updated;

            pendingChanges++;
            if (pendingChanges >= saveBatchSize)
            {
                await session.SaveChangesAsync(ct);
                pendingChanges = 0;
            }

            if (newAccountsTarget > 0 && discoveredAccounts >= newAccountsTarget)
            {
                logger.LogInformation(
                    "Discovery reached new accounts target ({Target}) for platform {Platform}. Stopping early.",
                    newAccountsTarget,
                    platformId);
                break;
            }
        }

        if (pendingChanges > 0)
        {
            await session.SaveChangesAsync(ct);
        }

        return summary;
    }

    private static async Task<Dictionary<Guid, RankSnapshot>> PreloadLatestSnapshotsAsync(
        IDataSession session,
        string platformId,
        IReadOnlyCollection<DiscoveredSummoner> discovered,
        CancellationToken ct)
    {
        var keys = discovered
            .Where(item => !string.IsNullOrWhiteSpace(item.Summoner.Puuid))
            .Select(item => new AccountKey(platformId, item.Summoner.Puuid))
            .Distinct()
            .ToList();

        if (keys.Count == 0)
        {
            return new Dictionary<Guid, RankSnapshot>();
        }

        var existing = await session.RiotAccounts.GetByKeysAsync(keys, ct);
        if (existing.Count == 0)
        {
            return new Dictionary<Guid, RankSnapshot>();
        }

        var existingIds = existing.Values.Select(account => account.Id).ToList();
        return await session.RankSnapshots.GetLatestForAccountsAsync(existingIds, ct);
    }

    private void LogPlatformSummary(PlatformSummary platformSummary)
    {
        // Named ops event (#444): one per platform per discovery run, so the
        // operator can follow ladder-discovery throughput from /ops/logs.
        logger.LogInformation(
            OpsEvents.DiscoveryCycleCompleted,
            "Discovery summary for {Platform}: accounts={AccountsProcessed}, newAccounts={NewAccounts}, candidatesInserted={Inserted}, candidatesUpdated={Updated}, rankSnapshotsInserted={RankInserted}, rankSnapshotsUnchanged={RankUnchanged}.",
            platformSummary.PlatformId,
            platformSummary.AccountsProcessed,
            platformSummary.NewAccountsDiscovered,
            platformSummary.CandidatesInserted,
            platformSummary.CandidatesUpdated,
            platformSummary.RankSnapshotsInserted,
            platformSummary.RankSnapshotsUnchanged);
    }

    private static object BuildSuccessPayload(IEnumerable<PlatformSummary> summaries)
    {
        return new
        {
            platforms = summaries.Select(summary => new
            {
                platform = summary.PlatformId,
                accountsProcessed = summary.AccountsProcessed,
                newAccounts = summary.NewAccountsDiscovered,
                candidatesInserted = summary.CandidatesInserted,
                candidatesUpdated = summary.CandidatesUpdated,
                rankSnapshotsInserted = summary.RankSnapshotsInserted,
                rankSnapshotsUnchanged = summary.RankSnapshotsUnchanged,
                // Null for platforms that completed; the per-platform error message
                // otherwise, so a partially failed run says which platform failed and why.
                error = summary.FailureReason
            })
        };
    }

    private sealed class PlatformSummary(string platformId)
    {
        public string PlatformId { get; } = platformId;
        public int AccountsProcessed { get; set; }
        public int NewAccountsDiscovered { get; set; }
        public int CandidatesInserted { get; set; }
        public int CandidatesUpdated { get; set; }
        public int RankSnapshotsInserted { get; set; }
        public int RankSnapshotsUnchanged { get; set; }
        public string? FailureReason { get; init; }
    }
}
