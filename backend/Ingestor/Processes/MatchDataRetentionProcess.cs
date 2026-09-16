using Core.Options;
using Data;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Components.Intake;
using Ingestor.Processes.Components.MatchIngestion;
using Ingestor.Processes.Components.Retention;
using Ingestor.Processes.Summaries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public sealed class MatchDataRetentionProcess(
    ILogger<MatchDataRetentionProcess> logger,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    IDataSessionFactory sessionFactory,
    TimeProvider timeProvider,
    IOptions<MatchDataRetentionOptions> retentionOptions,
    IOptions<MainAnalysisOptions> mainAnalysisOptions,
    IOptions<CandidatePruningOptions> candidatePruningOptions,
    IOptions<IntakeOptions> intakeOptions) : IIngestorProcess
{
    public string Name => "MatchDataRetention";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        // Prune stale never-promoted candidates first (#487) — independent of match
        // retention, so it runs even when there is nothing to delete below.
        var prunedCandidates = await PruneStaleCandidatesAsync(ct);

        // Then bound the queue itself (#1361). Pruning only ever removed never-promoted
        // candidates, so it could not touch the 773 k rows sitting in Queued — a queue the
        // claim drains at ~22 accounts per cycle, i.e. faster than the pipeline could ever
        // consume even if nothing else were added.
        var demotedCandidates = await DemoteExcessQueuedCandidatesAsync(ct);

        var retentionPlan = await LoadRetentionPlanAsync(ct);

        // Patch-window pruning of the tracked queue: keep the last N patches per
        // platform, delete older ones. Skipped when nothing is out of window.
        var patchDeletion = retentionPlan.DeletableMatchIds.Count == 0
            ? DeletionResult.Empty
            : await DeleteExpiredMatchDataAsync(retentionPlan.DeletableMatchIds, ct);

        // Drain every queue other than the tracked one. The site only serves ranked
        // solo/duo (all aggregates, the leaderboard and the champion pages are scoped
        // to it), so non-ranked matches have no downstream consumer and otherwise grow
        // unbounded — retention never considered them before (#680).
        var nonRankedDeletion = await DeleteNonRankedMatchDataAsync(retentionPlan.QueueId, ct);

        var deletedMatches = patchDeletion.DeletedMatches + nonRankedDeletion.DeletedMatches;
        var deletedParticipants = patchDeletion.DeletedParticipants + nonRankedDeletion.DeletedParticipants;

        if (deletedMatches > 0 || deletedParticipants > 0)
        {
            logger.LogInformation(
                "Match data retention removed {DeletedMatches} matches and {DeletedParticipants} participants "
                + "({NonRankedMatches} non-ranked) while keeping patches {RetainedPatches}.",
                deletedMatches,
                deletedParticipants,
                nonRankedDeletion.DeletedMatches,
                string.Join(
                    ", ",
                    retentionPlan.RetainedPatchesByPlatform
                        .OrderBy(entry => entry.Key)
                        .Select(entry => $"{entry.Key}=[{string.Join("|", entry.Value.Order())}]")));
        }

        var aggregateDeletion = await AggregateRetention.DeleteExpiredAsync(
            dbContextFactory, retentionOptions.Value.AggregateRetainedPatchCount, logger, ct);

        // Prune the legacy dense per-minute snapshot grid down to the canonical marks
        // (#694). Independent of the deletions above.
        var snapshotPrune = await PruneTimelineSnapshotsAsync(retentionPlan.QueueId, ct);

        return BuildRetentionPayload(
            retentionPlan,
            deletedMatches,
            deletedParticipants,
            nonRankedDeletion.DeletedMatches,
            prunedCandidates,
            demotedCandidates,
            aggregateDeletion,
            snapshotPrune);
    }

    /// <summary>
    /// Reduces the timeline snapshots of timeline-ingested matches to the
    /// <see cref="TimelineSnapshotBuilder.IntervalMinutes"/>, deleting every intermediate
    /// minute the dense grid used to store and flagging the match so it is never re-scanned. Batched, one transaction each:
    /// the first run backfills tens of millions of rows across the existing dense grid,
    /// so an unbounded delete would be a lock and WAL hazard — each committed batch frees
    /// space and lets an interrupted run resume. The IX_matches_snapshot_prune_pending
    /// partial index keeps the batch selection cheap and empties as pruning catches up.
    /// </summary>
    private async Task<SnapshotPruneResult> PruneTimelineSnapshotsAsync(int queueId, CancellationToken ct)
    {
        if (!retentionOptions.Value.PruneTimelineSnapshots)
        {
            return SnapshotPruneResult.Empty;
        }

        var batchSize = Math.Max(1, retentionOptions.Value.TimelineSnapshotPruneBatchSize);
        var prunedMatches = 0;
        var deletedSnapshots = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            var batchIds = await db.Matches
                .AsNoTracking()
                .Where(match => match.QueueId == queueId
                    && match.TimelineIngested
                    && !match.TimelineSnapshotsPruned)
                .OrderBy(match => match.Id)
                .Select(match => match.Id)
                .Take(batchSize)
                .ToListAsync(ct);

            if (batchIds.Count == 0)
            {
                break;
            }

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            deletedSnapshots += await db.MatchParticipantTimelineSnapshots
                .Where(snapshot => batchIds.Contains(snapshot.MatchId)
                    && !TimelineSnapshotBuilder.IntervalMinutes.Contains(snapshot.IntervalMinute))
                .ExecuteDeleteAsync(ct);
            prunedMatches += await db.Matches
                .Where(match => batchIds.Contains(match.Id))
                .ExecuteUpdateAsync(setters => setters.SetProperty(match => match.TimelineSnapshotsPruned, true), ct);
            await transaction.CommitAsync(ct);
        }

        if (prunedMatches > 0)
        {
            logger.LogInformation(
                "Timeline snapshot pruning reduced {PrunedMatches} match(es) to the canonical marks, "
                + "removing {DeletedSnapshots} intermediate-minute snapshot(s).",
                prunedMatches,
                deletedSnapshots);
        }

        return new SnapshotPruneResult(prunedMatches, deletedSnapshots);
    }

    private async Task<int> PruneStaleCandidatesAsync(CancellationToken ct)
    {
        var options = candidatePruningOptions.Value;
        if (!options.Enabled || options.PruneAfterDays <= 0)
        {
            return 0;
        }

        // TimeProvider, like every other time-dependent decision in the ingestor (#270): a
        // purge cutoff computed from DateTime.UtcNow cannot be frozen by a test.
        var cutoffUtc = timeProvider.GetUtcNow().UtcDateTime - TimeSpan.FromDays(options.PruneAfterDays);

        // The purge already lives on IDataSession.MainCandidates, so it is reached the way the
        // rest of the ingestor reaches candidate writes, instead of new-ing the repository here.
        await using var session = await sessionFactory.CreateAsync(ct);
        var pruned = await session.MainCandidates.PruneStaleNeverPromotedAsync(cutoffUtc, ct);

        if (pruned > 0)
        {
            logger.LogInformation(
                "Candidate pruning removed {PrunedCandidates} stale never-promoted candidate(s) inactive since before {Cutoff:o}.",
                pruned,
                cutoffUtc);
        }

        return pruned;
    }

    /// <summary>
    /// Caps how deep the <c>Queued</c> queue may get on any one platform (#1361), demoting the
    /// lowest-scored excess back to <c>Scored</c>.
    ///
    /// <para>
    /// A demotion is not a rejection: the candidate keeps its row and re-enters the promotion
    /// ranking on the next scoring pass, so this only decides <em>when</em> a candidate is in
    /// the claim's line of sight, never <em>whether</em> it ever will be. Deleting instead
    /// would throw away the only record that the player was seen at all — the same reasoning
    /// as #900's "deactivate, never delete".
    /// </para>
    ///
    /// <para>
    /// Bounded twice over: each statement touches at most
    /// <c>Intake:QueueDepthDemotionBatchSize</c> rows, and a run issues at most
    /// <c>Intake:MaxDemotionBatchesPerRun</c> of them per platform. The first drain of a
    /// backlog therefore spreads across cycles instead of putting a ~700 k-row UPDATE inside
    /// one 300 s command timeout (#988's lesson, applied to the candidate queue).
    /// </para>
    /// </summary>
    private async Task<int> DemoteExcessQueuedCandidatesAsync(CancellationToken ct)
    {
        var options = intakeOptions.Value;
        if (options.MaxQueuedPerPlatform <= 0 || options.MaxDemotionBatchesPerRun <= 0)
        {
            return 0;
        }

        await using var session = await sessionFactory.CreateAsync(ct);
        var depths = await session.MainCandidates.GetQueuedDepthByPlatformAsync(ct);

        var demoted = 0;
        foreach (var (platformId, depth) in depths.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var batches = QueueDepthDrain.PlanBatches(depth, options);
            if (batches.Count == 0)
            {
                continue;
            }

            var demotedOnPlatform = 0;
            foreach (var take in batches)
            {
                ct.ThrowIfCancellationRequested();

                var moved = await session.MainCandidates.DemoteLowestScoredQueuedAsync(platformId, take, ct);
                if (moved == 0)
                {
                    break;
                }

                demotedOnPlatform += moved;
            }

            demoted += demotedOnPlatform;
            logger.LogInformation(
                "Queue-depth cap on {Platform}: {Depth} queued candidate(s) against a cap of {Cap}; "
                + "demoted {Demoted} row(s) back to Scored this run, {Remaining} still over the cap.",
                platformId,
                depth,
                options.MaxQueuedPerPlatform,
                demotedOnPlatform,
                Math.Max(0, depth - options.MaxQueuedPerPlatform - demotedOnPlatform));
        }

        return demoted;
    }

    private async Task<RetentionPlan> LoadRetentionPlanAsync(CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var retainedPatchCount = Math.Max(1, retentionOptions.Value.RetainedPatchCount);
        var queueId = (int)mainAnalysisOptions.Value.QueueId;
        var retainedPatchesByPlatform = await RetainedPatchWindow.LoadAsync(db, queueId, retainedPatchCount, ct);
        var deletableMatchIds = retainedPatchesByPlatform.Count == 0
            ? []
            : await FindDeletableMatchIdsAsync(db, queueId, retainedPatchesByPlatform, ct);

        return new RetentionPlan(retainedPatchCount, queueId, retainedPatchesByPlatform, deletableMatchIds);
    }

    private static async Task<List<string>> FindDeletableMatchIdsAsync(
        TrueMainDbContext db,
        int queueId,
        IReadOnlyDictionary<string, HashSet<string>> retainedPatchesByPlatform,
        CancellationToken ct)
    {
        var deletableMatchIds = new List<string>();

        foreach (var (platformId, retainedPatches) in retainedPatchesByPlatform.OrderBy(entry => entry.Key))
        {
            var platformQuery = db.Matches
                .AsNoTracking()
                .Where(match => match.QueueId == queueId && match.PlatformId == platformId);

            foreach (var retainedPatch in retainedPatches)
            {
                // matches."Patch" is the stored generated major.minor of GameVersion
                // (#1368), so the live window is an indexed equality instead of the
                // pair of unindexable predicates this used to be
                // (GameVersion <> '16.4' AND GameVersion NOT LIKE '16.4.%'). Same
                // answer, including for a match whose version does not parse: its
                // Patch is NULL, and EF's null semantics keep it deletable exactly as
                // the two string comparisons did.
                platformQuery = platformQuery.Where(match => match.Patch != retainedPatch);
            }

            deletableMatchIds.AddRange(await platformQuery
                .Select(match => match.Id)
                .ToListAsync(ct));
        }

        return deletableMatchIds;
    }

    // Delete in bounded batches, one transaction each, mirroring the non-ranked
    // drain: a whole patch dropping out of the window is a patch's worth of matches,
    // and the cascading removal of timeline snapshots / kill positions / jungle
    // clears / perk selections / bans made the previous single-transaction delete
    // blow the command timeout on every run — and its rollback meant retention never
    // reclaimed anything (#988). Each committed batch keeps its progress and lets an
    // interrupted purge resume next run.
    private async Task<DeletionResult> DeleteExpiredMatchDataAsync(
        IReadOnlyCollection<string> deletableMatchIds,
        CancellationToken ct)
    {
        var batchSize = Math.Max(1, retentionOptions.Value.ExpiredPatchDeleteBatchSize);
        var deletedMatches = 0;
        var deletedParticipants = 0;

        foreach (var batchIds in deletableMatchIds.Chunk(batchSize))
        {
            ct.ThrowIfCancellationRequested();

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // MatchParticipant -> Match is Restrict, so participants must be deleted
            // before the match; the remaining child tables cascade on the match delete.
            deletedParticipants += await db.MatchParticipants
                .Where(participant => batchIds.Contains(participant.MatchId))
                .ExecuteDeleteAsync(ct);
            deletedMatches += await db.Matches
                .Where(match => batchIds.Contains(match.Id))
                .ExecuteDeleteAsync(ct);
            await transaction.CommitAsync(ct);
        }

        return new DeletionResult(deletedMatches, deletedParticipants);
    }

    private async Task<DeletionResult> DeleteNonRankedMatchDataAsync(int queueId, CancellationToken ct)
    {
        var batchSize = Math.Max(1, retentionOptions.Value.NonRankedDeleteBatchSize);
        var deletedMatches = 0;
        var deletedParticipants = 0;

        // Delete in bounded batches, one transaction each: the cascading removal of
        // timeline snapshots / kill positions / perk selections / bans makes
        // a single unbounded delete a lock and WAL hazard, especially right after a
        // disk-full incident. Each committed batch frees space and lets an interrupted
        // drain resume next run.
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            var batchIds = await db.Matches
                .AsNoTracking()
                .Where(match => match.QueueId != queueId)
                .OrderBy(match => match.Id)
                .Select(match => match.Id)
                .Take(batchSize)
                .ToListAsync(ct);

            if (batchIds.Count == 0)
            {
                break;
            }

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // MatchParticipant -> Match is Restrict, so participants must be deleted
            // before the match; the remaining child tables cascade on the match delete.
            deletedParticipants += await db.MatchParticipants
                .Where(participant => batchIds.Contains(participant.MatchId))
                .ExecuteDeleteAsync(ct);
            deletedMatches += await db.Matches
                .Where(match => batchIds.Contains(match.Id))
                .ExecuteDeleteAsync(ct);
            await transaction.CommitAsync(ct);
        }

        return new DeletionResult(deletedMatches, deletedParticipants);
    }

    private static MatchDataRetentionSummary BuildRetentionPayload(
        RetentionPlan retentionPlan,
        int deletedMatches,
        int deletedParticipants,
        int deletedNonRankedMatches,
        int prunedCandidates,
        int demotedQueuedCandidates,
        AggregateRetention.AggregateDeletionResult aggregateDeletion,
        SnapshotPruneResult snapshotPrune)
    {
        return new MatchDataRetentionSummary(
            retentionPlan.RetainedPatchCount,
            retentionPlan.QueueId,
            deletedMatches,
            deletedParticipants,
            deletedNonRankedMatches,
            prunedCandidates,
            demotedQueuedCandidates,
            snapshotPrune.PrunedMatches,
            snapshotPrune.DeletedSnapshots,
            aggregateDeletion.DeletedScopes,
            aggregateDeletion.DeletedMatchupStats,
            aggregateDeletion.DeletedSynergyStats,
            aggregateDeletion.DeletedBanStats,
            retentionPlan.RetainedPatchesByPlatform
                .OrderBy(entry => entry.Key)
                .Select(entry => new RetainedPatchesSummary(entry.Key, entry.Value.Order().ToList()))
                .ToList());
    }

    private sealed record SnapshotPruneResult(int PrunedMatches, int DeletedSnapshots)
    {
        public static SnapshotPruneResult Empty { get; } = new(0, 0);
    }

    private sealed record DeletionResult(int DeletedMatches, int DeletedParticipants)
    {
        public static DeletionResult Empty { get; } = new(0, 0);
    }

    private sealed record RetentionPlan(
        int RetainedPatchCount,
        int QueueId,
        IReadOnlyDictionary<string, HashSet<string>> RetainedPatchesByPlatform,
        IReadOnlyList<string> DeletableMatchIds);
}
