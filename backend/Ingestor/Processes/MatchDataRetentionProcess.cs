using Core.Lol.Patches;
using Core.Options;
using Data;
using Ingestor.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

public sealed class MatchDataRetentionProcess(
    ILogger<MatchDataRetentionProcess> logger,
    IDbContextFactory<TrueMainDbContext> dbContextFactory,
    IOptions<MatchDataRetentionOptions> retentionOptions,
    IOptions<MainAnalysisOptions> mainAnalysisOptions,
    IOptions<CandidatePruningOptions> candidatePruningOptions) : IIngestorProcess
{
    public string Name => "MatchDataRetention";

    // The marks kept forever: the timeline-leads / matchup-lead aggregations only
    // read these, and match-detail reads minute 15. The dense per-minute grid in
    // between exists solely to feed the one-shot powerspike aggregation and is pruned
    // once a match is folded. Mirrors ChampionMatchupLeadAggregationProcess.
    private static readonly int[] CanonicalSnapshotMinutes = [5, 10, 15, 20, 30];

    public async Task<object?> RunCoreAsync(CancellationToken ct)
    {
        // Prune stale never-promoted candidates first (#487) — independent of match
        // retention, so it runs even when there is nothing to delete below.
        var prunedCandidates = await PruneStaleCandidatesAsync(ct);

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

        var aggregateDeletion = await DeleteExpiredAggregatesAsync(ct);

        // Prune the dense per-minute snapshot grid of already-aggregated matches down
        // to the canonical marks — the storage the powerspike pre-aggregation (#694)
        // was built to reclaim. Independent of the deletions above.
        var snapshotPrune = await PruneAggregatedTimelineSnapshotsAsync(retentionPlan.QueueId, ct);

        return BuildRetentionPayload(
            retentionPlan,
            deletedMatches,
            deletedParticipants,
            nonRankedDeletion.DeletedMatches,
            prunedCandidates,
            aggregateDeletion,
            snapshotPrune);
    }

    /// <summary>
    /// Reduces the timeline snapshots of matches already folded into the powerspike
    /// aggregates (<see cref="Data.Entities.Match.PowerspikeAggregated"/>) to the
    /// <see cref="CanonicalSnapshotMinutes"/>, deleting every intermediate minute and
    /// flagging the match so it is never re-scanned. Batched, one transaction each:
    /// the first run backfills tens of millions of rows across the existing dense grid,
    /// so an unbounded delete would be a lock and WAL hazard — each committed batch frees
    /// space and lets an interrupted run resume. The IX_matches_snapshot_prune_pending
    /// partial index keeps the batch selection cheap and empties as pruning catches up.
    /// </summary>
    private async Task<SnapshotPruneResult> PruneAggregatedTimelineSnapshotsAsync(int queueId, CancellationToken ct)
    {
        if (!retentionOptions.Value.PruneAggregatedTimelineSnapshots)
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
                    && match.PowerspikeAggregated
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
                    && !CanonicalSnapshotMinutes.Contains(snapshot.IntervalMinute))
                .ExecuteDeleteAsync(ct);
            prunedMatches += await db.Matches
                .Where(match => batchIds.Contains(match.Id))
                .ExecuteUpdateAsync(setters => setters.SetProperty(match => match.TimelineSnapshotsPruned, true), ct);
            await transaction.CommitAsync(ct);
        }

        if (prunedMatches > 0)
        {
            logger.LogInformation(
                "Timeline snapshot pruning reduced {PrunedMatches} aggregated match(es) to the canonical marks, "
                + "removing {DeletedSnapshots} intermediate-minute snapshot(s).",
                prunedMatches,
                deletedSnapshots);
        }

        return new SnapshotPruneResult(prunedMatches, deletedSnapshots);
    }

    /// <summary>
    /// Deletes champion aggregates for patches older than the
    /// <see cref="MatchDataRetentionOptions.AggregateRetainedPatchCount"/> most
    /// recent ones. Disabled by default (0): aggregates are the site's frozen
    /// patch history (#466) and can never be recomputed once their raw matches
    /// are retired, so only small environments (preprod) opt in.
    /// </summary>
    private async Task<AggregateDeletionResult> DeleteExpiredAggregatesAsync(CancellationToken ct)
    {
        var retainedPatchCount = retentionOptions.Value.AggregateRetainedPatchCount;
        if (retainedPatchCount <= 0)
        {
            return AggregateDeletionResult.Empty;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var observedPatches = new HashSet<string>(StringComparer.Ordinal);
        observedPatches.UnionWith(await db.ChampionAggregateScopes
            .AsNoTracking().Select(scope => scope.GameVersion).Distinct().ToListAsync(ct));
        observedPatches.UnionWith(await db.ChampionMatchupStats
            .AsNoTracking().Select(stat => stat.Patch).Distinct().ToListAsync(ct));
        observedPatches.UnionWith(await db.ChampionTimelineLeadStats
            .AsNoTracking().Select(stat => stat.Patch).Distinct().ToListAsync(ct));
        observedPatches.UnionWith(await db.ChampionPowerspikeCurveStats
            .AsNoTracking().Select(stat => stat.Patch).Distinct().ToListAsync(ct));
        observedPatches.UnionWith(await db.ChampionPowerspikeEventStats
            .AsNoTracking().Select(stat => stat.Patch).Distinct().ToListAsync(ct));

        // Rank the observed patch strings by parsed version and keep the N most
        // recent. Unparseable strings are never deleted — better to leave an odd
        // row behind than to wipe data on a format surprise.
        var parsedPatches = observedPatches
            .Select(raw => PatchVersion.TryParse(raw, out var version)
                ? (Raw: raw, Version: version)
                : default((string Raw, PatchVersion Version)?))
            .Where(entry => entry is not null)
            .Select(entry => entry!.Value)
            .ToList();

        var retainedVersions = parsedPatches
            .Select(entry => new PatchVersion(entry.Version.Major, entry.Version.Minor))
            .Distinct()
            .OrderDescending()
            .Take(retainedPatchCount)
            .ToHashSet();

        var stalePatches = parsedPatches
            .Where(entry => !retainedVersions.Contains(new PatchVersion(entry.Version.Major, entry.Version.Minor)))
            .Select(entry => entry.Raw)
            .Order(StringComparer.Ordinal)
            .ToList();

        if (stalePatches.Count == 0)
        {
            return AggregateDeletionResult.Empty;
        }

        var result = AggregateDeletionResult.Empty;

        // One patch per transaction keeps each delete's lock footprint and WAL
        // bounded — a scope delete cascades to its pattern rows, and years of
        // frozen patches could otherwise pile into one huge transaction — while
        // a patch's five tables still go together (no half-deleted patch left
        // behind by an interruption). Global champion_dim_* rows are left
        // alone: they are deduplicated across patches and other scopes may
        // still reference them.
        foreach (var stalePatch in stalePatches)
        {
            ct.ThrowIfCancellationRequested();

            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            result = new AggregateDeletionResult(
                result.DeletedScopes + await db.ChampionAggregateScopes
                    .Where(scope => scope.GameVersion == stalePatch).ExecuteDeleteAsync(ct),
                result.DeletedMatchupStats + await db.ChampionMatchupStats
                    .Where(stat => stat.Patch == stalePatch).ExecuteDeleteAsync(ct),
                result.DeletedTimelineLeadStats + await db.ChampionTimelineLeadStats
                    .Where(stat => stat.Patch == stalePatch).ExecuteDeleteAsync(ct),
                result.DeletedPowerspikeCurveStats + await db.ChampionPowerspikeCurveStats
                    .Where(stat => stat.Patch == stalePatch).ExecuteDeleteAsync(ct),
                result.DeletedPowerspikeEventStats + await db.ChampionPowerspikeEventStats
                    .Where(stat => stat.Patch == stalePatch).ExecuteDeleteAsync(ct));
            await transaction.CommitAsync(ct);
        }

        if (result.TotalDeleted > 0)
        {
            logger.LogInformation(
                "Aggregate retention removed {DeletedScopes} scopes, {DeletedMatchups} matchup, "
                + "{DeletedLeads} timeline-lead and {DeletedPowerspikes} powerspike rows for stale patches "
                + "{StalePatches} (keeping {RetainedPatches}).",
                result.DeletedScopes,
                result.DeletedMatchupStats,
                result.DeletedTimelineLeadStats,
                result.DeletedPowerspikeCurveStats + result.DeletedPowerspikeEventStats,
                string.Join("|", stalePatches),
                string.Join("|", retainedVersions.OrderDescending().Select(version => version.ToString())));
        }

        return result;
    }

    private async Task<int> PruneStaleCandidatesAsync(CancellationToken ct)
    {
        var options = candidatePruningOptions.Value;
        if (!options.Enabled || options.PruneAfterDays <= 0)
        {
            return 0;
        }

        var cutoffUtc = DateTime.UtcNow - TimeSpan.FromDays(options.PruneAfterDays);
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var pruned = await new Data.Repositories.MainCandidateRepository(db)
            .PruneStaleNeverPromotedAsync(cutoffUtc, ct);

        if (pruned > 0)
        {
            logger.LogInformation(
                "Candidate pruning removed {PrunedCandidates} stale never-promoted candidate(s) inactive since before {Cutoff:o}.",
                pruned,
                cutoffUtc);
        }

        return pruned;
    }

    private async Task<RetentionPlan> LoadRetentionPlanAsync(CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var retainedPatchCount = Math.Max(1, retentionOptions.Value.RetainedPatchCount);
        var queueId = (int)mainAnalysisOptions.Value.QueueId;
        var observedMatches = await LoadObservedPatchesAsync(db, queueId, ct);
        var retainedPatchesByPlatform = ComputeRetainedPatchesByPlatform(observedMatches, retainedPatchCount);
        var deletableMatchIds = retainedPatchesByPlatform.Count == 0
            ? []
            : await FindDeletableMatchIdsAsync(db, queueId, retainedPatchesByPlatform, ct);

        return new RetentionPlan(retainedPatchCount, queueId, retainedPatchesByPlatform, deletableMatchIds);
    }

    private static Task<List<ObservedMatch>> LoadObservedPatchesAsync(
        TrueMainDbContext db,
        int queueId,
        CancellationToken ct)
    {
        return db.Matches
            .AsNoTracking()
            .Where(match => match.QueueId == queueId)
            .OrderByDescending(match => match.GameStartTimeUtc)
            .Select(match => new ObservedMatch(match.PlatformId, match.GameVersion))
            .ToListAsync(ct);
    }

    private static Dictionary<string, HashSet<string>> ComputeRetainedPatchesByPlatform(
        IReadOnlyCollection<ObservedMatch> observedMatches,
        int retainedPatchCount)
    {
        return observedMatches
            .GroupBy(match => match.PlatformId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(match => PatchVersion.TryParse(match.GameVersion, out var patch)
                        ? patch.ToMajorMinor()
                        : null)
                    .Where(patch => !string.IsNullOrWhiteSpace(patch))
                    .Select(patch => patch!)
                    .Distinct()
                    .Take(retainedPatchCount)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
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
                var patchPrefix = $"{retainedPatch}.%";
                platformQuery = platformQuery
                    .Where(match => match.GameVersion != retainedPatch
                        && !EF.Functions.Like(match.GameVersion, patchPrefix));
            }

            deletableMatchIds.AddRange(await platformQuery
                .Select(match => match.Id)
                .ToListAsync(ct));
        }

        return deletableMatchIds;
    }

    private async Task<DeletionResult> DeleteExpiredMatchDataAsync(
        IReadOnlyCollection<string> deletableMatchIds,
        CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var deletedParticipants = await db.MatchParticipants
            .Where(participant => deletableMatchIds.Contains(participant.MatchId))
            .ExecuteDeleteAsync(ct);
        var deletedMatches = await db.Matches
            .Where(match => deletableMatchIds.Contains(match.Id))
            .ExecuteDeleteAsync(ct);
        await transaction.CommitAsync(ct);

        return new DeletionResult(deletedMatches, deletedParticipants);
    }

    private async Task<DeletionResult> DeleteNonRankedMatchDataAsync(int queueId, CancellationToken ct)
    {
        var batchSize = Math.Max(1, retentionOptions.Value.NonRankedDeleteBatchSize);
        var deletedMatches = 0;
        var deletedParticipants = 0;

        // Delete in bounded batches, one transaction each: the cascading removal of
        // timeline snapshots / kill positions / jungle clears / perk selections makes
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

    private static object BuildRetentionPayload(
        RetentionPlan retentionPlan,
        int deletedMatches,
        int deletedParticipants,
        int deletedNonRankedMatches,
        int prunedCandidates,
        AggregateDeletionResult aggregateDeletion,
        SnapshotPruneResult snapshotPrune)
    {
        return new
        {
            retainedPatchCount = retentionPlan.RetainedPatchCount,
            queueId = retentionPlan.QueueId,
            deletedMatches,
            deletedParticipants,
            deletedNonRankedMatches,
            prunedCandidates,
            prunedSnapshotMatches = snapshotPrune.PrunedMatches,
            deletedIntermediateSnapshots = snapshotPrune.DeletedSnapshots,
            deletedAggregateScopes = aggregateDeletion.DeletedScopes,
            deletedMatchupStats = aggregateDeletion.DeletedMatchupStats,
            deletedTimelineLeadStats = aggregateDeletion.DeletedTimelineLeadStats,
            deletedPowerspikeCurveStats = aggregateDeletion.DeletedPowerspikeCurveStats,
            deletedPowerspikeEventStats = aggregateDeletion.DeletedPowerspikeEventStats,
            retainedPatchesByPlatform = retentionPlan.RetainedPatchesByPlatform
                .OrderBy(entry => entry.Key)
                .Select(entry => new
                {
                    platformId = entry.Key,
                    patches = entry.Value.Order().ToArray()
                })
                .ToArray()
        };
    }

    private sealed record ObservedMatch(string PlatformId, string GameVersion);

    private sealed record SnapshotPruneResult(int PrunedMatches, int DeletedSnapshots)
    {
        public static SnapshotPruneResult Empty { get; } = new(0, 0);
    }

    private sealed record DeletionResult(int DeletedMatches, int DeletedParticipants)
    {
        public static DeletionResult Empty { get; } = new(0, 0);
    }

    private sealed record AggregateDeletionResult(
        int DeletedScopes,
        int DeletedMatchupStats,
        int DeletedTimelineLeadStats,
        int DeletedPowerspikeCurveStats,
        int DeletedPowerspikeEventStats)
    {
        public static AggregateDeletionResult Empty { get; } = new(0, 0, 0, 0, 0);

        public int TotalDeleted
            => DeletedScopes
                + DeletedMatchupStats
                + DeletedTimelineLeadStats
                + DeletedPowerspikeCurveStats
                + DeletedPowerspikeEventStats;
    }

    private sealed record RetentionPlan(
        int RetainedPatchCount,
        int QueueId,
        IReadOnlyDictionary<string, HashSet<string>> RetainedPatchesByPlatform,
        IReadOnlyList<string> DeletableMatchIds);
}
