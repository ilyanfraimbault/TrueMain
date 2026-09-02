using Core.Lol.Patches;
using Core.Options;
using Data;
using Data.Repositories;
using Ingestor.Options;
using Ingestor.Processes.Components.Intake;
using Ingestor.Processes.Summaries;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

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

    // The marks kept forever: match-detail reads minute 15 for the laning-phase diff.
    // The dense per-minute grid in between exists solely to feed the one-shot
    // powerspike aggregation and is pruned once a match is folded. The other reader
    // of these marks, the timeline-leads aggregate, was dropped in #889.
    private static readonly int[] CanonicalSnapshotMinutes = [5, 10, 15, 20, 30];

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

        var aggregateDeletion = await DeleteExpiredAggregatesAsync(ct);

        // Prune the dense per-minute snapshot grid of already-aggregated matches down
        // to the canonical marks — the storage the powerspike pre-aggregation (#694)
        // was built to reclaim. Independent of the deletions above.
        var snapshotPrune = await PruneAggregatedTimelineSnapshotsAsync(retentionPlan.QueueId, ct);

        // Roll the per-opponent powerspike split back up once a patch stops receiving
        // games, then reclaim the long tail of rare core builds. Order matters: the
        // floor below must see the rolled-up games, not the per-opponent shards.
        var collapsedPowerspikeOpponents = await CollapseOutOfWindowPowerspikeOpponentsAsync(retentionPlan, ct);
        var prunedPowerspikeEvents = await PruneSubFloorPowerspikeEventsAsync(retentionPlan, ct);

        return BuildRetentionPayload(
            retentionPlan,
            deletedMatches,
            deletedParticipants,
            nonRankedDeletion.DeletedMatches,
            prunedCandidates,
            demotedCandidates,
            aggregateDeletion,
            snapshotPrune,
            prunedPowerspikeEvents,
            collapsedPowerspikeOpponents);
    }

    /// <summary>
    /// Rolls the per-opponent powerspike event rows (#957) of a patch back into a
    /// single <c>OpponentChampionId = 0</c> row once that patch drops out of the
    /// retained window.
    ///
    /// <para>
    /// Two things would otherwise break. The split multiplies the row count by the
    /// number of distinct lane opponents met, and nothing would ever reclaim it —
    /// the opponent dimension is only ever queried for the patch the champion page
    /// is showing, so keeping it on frozen patches is pure weight. And
    /// <see cref="PruneSubFloorPowerspikeEventsAsync"/> filters on a row's own
    /// <c>Games</c>: a build with 500 games split across 40 opponents becomes 40
    /// rows of ~12, every one of them under the floor, so the next retention cycle
    /// would delete the patch's spikes outright — including from the unscoped read,
    /// which today keeps them. Collapsing first restores exactly the pre-#957 row,
    /// so that floor sees the same number it used to and behaves unchanged.
    /// </para>
    ///
    /// <para>
    /// Unbatched, unlike the snapshot prune: this touches one expiring patch's
    /// shards rather than a whole backfilled history, and it has nothing to do on
    /// the first runs after #957 ships (no row carries an opponent yet).
    /// </para>
    /// </summary>
    private async Task<PowerspikeCollapseResult> CollapseOutOfWindowPowerspikeOpponentsAsync(
        RetentionPlan retentionPlan,
        CancellationToken ct)
    {
        var livePatches = retentionPlan.RetainedPatchesByPlatform
            .SelectMany(entry => entry.Value)
            .ToArray();
        if (livePatches.Length == 0)
        {
            // No live patch resolved means the plan could not decide what is frozen;
            // collapsing everything on that basis would destroy the split for patches
            // still in use. Do nothing rather than guess.
            return PowerspikeCollapseResult.Empty;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Fold every shard of a frozen patch into its opponent-less row, creating it
        // when the patch predates #957 entirely. Additive, so re-running is safe as
        // long as the shards are deleted in the same transaction (below).
        const string collapseSql = """
            INSERT INTO champion_powerspike_event_stats
                ("Id", "ChampionId", "TeamPosition", "Patch", "elo_bracket",
                 "BuildFirstItemId", "BuildKeystoneId", "OpponentChampionId",
                 "EventType", "RefId", "SumSpike", "SumMinute", "Games", "AggregatedAtUtc")
            SELECT gen_random_uuid(), s."ChampionId", s."TeamPosition", s."Patch", s."elo_bracket",
                   s."BuildFirstItemId", s."BuildKeystoneId", 0,
                   s."EventType", s."RefId", SUM(s."SumSpike"), SUM(s."SumMinute"), SUM(s."Games"),
                   MAX(s."AggregatedAtUtc")
            FROM champion_powerspike_event_stats s
            WHERE s."OpponentChampionId" <> 0 AND NOT (s."Patch" = ANY(@livePatches))
            GROUP BY s."ChampionId", s."TeamPosition", s."Patch", s."elo_bracket",
                     s."BuildFirstItemId", s."BuildKeystoneId", s."EventType", s."RefId"
            ON CONFLICT ("ChampionId", "TeamPosition", "Patch", "elo_bracket",
                         "BuildFirstItemId", "BuildKeystoneId", "OpponentChampionId",
                         "EventType", "RefId") DO UPDATE SET
                "SumSpike" = champion_powerspike_event_stats."SumSpike" + EXCLUDED."SumSpike",
                "SumMinute" = champion_powerspike_event_stats."SumMinute" + EXCLUDED."SumMinute",
                "Games" = champion_powerspike_event_stats."Games" + EXCLUDED."Games",
                -- GREATEST rather than EXCLUDED: the surviving opponent-less row can be
                -- the older of the two (it predates #957 entirely), and this column is
                -- read as "when was this last folded into".
                "AggregatedAtUtc" = GREATEST(
                    champion_powerspike_event_stats."AggregatedAtUtc", EXCLUDED."AggregatedAtUtc")
            """;

        var collapsedGroups = await db.Database.ExecuteSqlRawAsync(
            collapseSql, [new NpgsqlParameter("livePatches", livePatches)], ct);

        var deletedShards = 0;
        if (collapsedGroups > 0)
        {
            // Same predicate as the SELECT above and the same transaction snapshot, so
            // every shard that contributed is removed and none that did not. The
            // freshly written rows carry opponent 0 and are excluded by construction.
            deletedShards = await db.ChampionPowerspikeEventStats
                .Where(stat => stat.OpponentChampionId != 0 && !livePatches.Contains(stat.Patch))
                .ExecuteDeleteAsync(ct);
        }

        await transaction.CommitAsync(ct);

        if (deletedShards > 0)
        {
            logger.LogInformation(
                "Powerspike opponent retention collapsed {DeletedShards} per-opponent row(s) into "
                + "{CollapsedGroups} opponent-less row(s) on patches outside {LivePatches}.",
                deletedShards,
                collapsedGroups,
                string.Join("|", livePatches.Order()));
        }

        return new PowerspikeCollapseResult(collapsedGroups, deletedShards);
    }

    /// <summary>
    /// Deletes <c>champion_powerspike_event_stats</c> rows that sit below the read's
    /// games floor, restricted to patches that no longer receive games (#890).
    ///
    /// The restriction is the point: those rows are additive and accumulate over a
    /// patch's life, so pruning a live patch would delete a build that is slowly on
    /// its way to the floor and reset it to zero every cycle — it could never get
    /// there. Once a patch drops out of the retained window nothing folds into it
    /// again, its sub-floor rows are frozen below a threshold the read already
    /// filters on, and they are pure dead weight.
    /// </summary>
    private async Task<int> PruneSubFloorPowerspikeEventsAsync(RetentionPlan retentionPlan, CancellationToken ct)
    {
        var minGames = retentionOptions.Value.PowerspikeEventMinGames;
        if (minGames <= 0)
        {
            return 0;
        }

        var livePatches = retentionPlan.RetainedPatchesByPlatform
            .SelectMany(entry => entry.Value)
            .ToHashSet(StringComparer.Ordinal);
        if (livePatches.Count == 0)
        {
            return 0;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var deleted = await db.ChampionPowerspikeEventStats
            .Where(stat => stat.Games < minGames && !livePatches.Contains(stat.Patch))
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
        {
            logger.LogInformation(
                "Powerspike event retention removed {Deleted} sub-floor row(s) (<{MinGames} games) "
                + "on patches outside {LivePatches}.",
                deleted,
                minGames,
                string.Join("|", livePatches.Order()));
        }

        return deleted;
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
        observedPatches.UnionWith(await db.ChampionPowerspikeCurveStats
            .AsNoTracking().Select(stat => stat.Patch).Distinct().ToListAsync(ct));
        observedPatches.UnionWith(await db.ChampionPowerspikeEventStats
            .AsNoTracking().Select(stat => stat.Patch).Distinct().ToListAsync(ct));
        observedPatches.UnionWith(await db.ChampionSynergyStats
            .AsNoTracking().Select(stat => stat.Patch).Distinct().ToListAsync(ct));
        observedPatches.UnionWith(await db.ChampionSynergyBaselineStats
            .AsNoTracking().Select(stat => stat.Patch).Distinct().ToListAsync(ct));
        observedPatches.UnionWith(await db.ChampionBanStats
            .AsNoTracking().Select(stat => stat.Patch).Distinct().ToListAsync(ct));
        observedPatches.UnionWith(await db.BanScopeTotals
            .AsNoTracking().Select(total => total.Patch).Distinct().ToListAsync(ct));

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
                result.DeletedPowerspikeCurveStats + await db.ChampionPowerspikeCurveStats
                    .Where(stat => stat.Patch == stalePatch).ExecuteDeleteAsync(ct),
                result.DeletedPowerspikeEventStats + await db.ChampionPowerspikeEventStats
                    .Where(stat => stat.Patch == stalePatch).ExecuteDeleteAsync(ct),
                // The synergy pair rows and the baselines they are divided by go in
                // the same transaction as each other: a patch left with baselines but
                // no pairs (or the reverse) would still be read, and would answer with
                // an expected win rate computed against a cohort that no longer exists.
                result.DeletedSynergyStats + await db.ChampionSynergyStats
                    .Where(stat => stat.Patch == stalePatch).ExecuteDeleteAsync(ct)
                    + await db.ChampionSynergyBaselineStats
                        .Where(stat => stat.Patch == stalePatch).ExecuteDeleteAsync(ct),
                // Same reasoning as the synergy pair: the ban counts and the match
                // totals they are divided by must leave together, or the survivor
                // would be read as a rate over a denominator that is gone.
                result.DeletedBanStats + await db.ChampionBanStats
                    .Where(stat => stat.Patch == stalePatch).ExecuteDeleteAsync(ct)
                    + await db.BanScopeTotals
                        .Where(total => total.Patch == stalePatch).ExecuteDeleteAsync(ct));
            await transaction.CommitAsync(ct);
        }

        if (result.TotalDeleted > 0)
        {
            logger.LogInformation(
                "Aggregate retention removed {DeletedScopes} scopes, {DeletedMatchups} matchup, "
                + "{DeletedPowerspikes} powerspike, {DeletedSynergies} synergy and {DeletedBans} ban "
                + "rows for stale patches {StalePatches} (keeping {RetainedPatches}).",
                result.DeletedScopes,
                result.DeletedMatchupStats,
                result.DeletedPowerspikeCurveStats + result.DeletedPowerspikeEventStats,
                result.DeletedSynergyStats,
                result.DeletedBanStats,
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
        var observedMatches = await LoadObservedPatchesAsync(db, queueId, ct);
        var retainedPatchesByPlatform = ComputeRetainedPatchesByPlatform(observedMatches, retainedPatchCount);
        var deletableMatchIds = retainedPatchesByPlatform.Count == 0
            ? []
            : await FindDeletableMatchIdsAsync(db, queueId, retainedPatchesByPlatform, ct);

        return new RetentionPlan(retainedPatchCount, queueId, retainedPatchesByPlatform, deletableMatchIds);
    }

    /// <summary>
    /// The distinct (platform, game version) pairs of the retained queue, each with the
    /// start time of its most recent match.
    ///
    /// <para>
    /// Grouped server-side on purpose: the plan only needs the couple of newest patches per
    /// platform, but the table holds hundreds of thousands of matches, and projecting one
    /// row per match pulled the whole retained history into memory on every retention run.
    /// The <c>PatchVersion</c> normalisation below is not translatable to SQL, but the
    /// <c>GROUP BY (platform_id, game_version)</c> and its <c>max(game_start_time_utc)</c>
    /// are, and they return a few hundred rows instead.
    /// </para>
    ///
    /// <para>
    /// Ordering by that maximum is equivalent to the previous per-match ordering: a patch's
    /// first appearance in a descending match list is exactly its most recent match, and a
    /// normalised patch's most recent match is the newest across the game versions that
    /// normalise to it.
    /// </para>
    /// </summary>
    private static Task<List<ObservedPatch>> LoadObservedPatchesAsync(
        TrueMainDbContext db,
        int queueId,
        CancellationToken ct)
    {
        return ObservedPatchesQuery(db, queueId).ToListAsync(ct);
    }

    /// <summary>
    /// The query behind <see cref="LoadObservedPatchesAsync"/>, exposed so a test can assert
    /// on the SQL it translates to: the whole point of the shape is that Postgres does the
    /// grouping, and a client-evaluated fallback would silently read the table again.
    /// </summary>
    internal static IQueryable<ObservedPatch> ObservedPatchesQuery(TrueMainDbContext db, int queueId)
    {
        return db.Matches
            .AsNoTracking()
            .Where(match => match.QueueId == queueId)
            .GroupBy(match => new { match.PlatformId, match.GameVersion })
            .Select(group => new ObservedPatch(
                group.Key.PlatformId,
                group.Key.GameVersion,
                group.Max(match => match.GameStartTimeUtc)));
    }

    internal static Dictionary<string, HashSet<string>> ComputeRetainedPatchesByPlatform(
        IReadOnlyCollection<ObservedPatch> observedPatches,
        int retainedPatchCount)
    {
        return observedPatches
            .GroupBy(observed => observed.PlatformId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    // Newest patch first, with the game version as a tie-breaker so two
                    // versions sharing a last-seen timestamp keep a stable order.
                    .OrderByDescending(observed => observed.LastGameStartTimeUtc)
                    .ThenByDescending(observed => observed.GameVersion, StringComparer.Ordinal)
                    .Select(observed => PatchVersion.TryParse(observed.GameVersion, out var patch)
                        ? patch.ToMajorMinor()
                        : null)
                    .Where(patch => !string.IsNullOrWhiteSpace(patch))
                    .Select(patch => patch!)
                    .Distinct(StringComparer.Ordinal)
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
        AggregateDeletionResult aggregateDeletion,
        SnapshotPruneResult snapshotPrune,
        int prunedPowerspikeEvents,
        PowerspikeCollapseResult powerspikeCollapse)
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
            aggregateDeletion.DeletedPowerspikeCurveStats,
            aggregateDeletion.DeletedPowerspikeEventStats,
            aggregateDeletion.DeletedSynergyStats,
            aggregateDeletion.DeletedBanStats,
            prunedPowerspikeEvents,
            powerspikeCollapse.DeletedShards,
            powerspikeCollapse.CollapsedGroups,
            retentionPlan.RetainedPatchesByPlatform
                .OrderBy(entry => entry.Key)
                .Select(entry => new RetainedPatchesSummary(entry.Key, entry.Value.Order().ToList()))
                .ToList());
    }

    /// <summary>One observed (platform, game version) pair and the start time of its newest match.</summary>
    internal sealed record ObservedPatch(string PlatformId, string GameVersion, DateTime LastGameStartTimeUtc);

    private sealed record SnapshotPruneResult(int PrunedMatches, int DeletedSnapshots)
    {
        public static SnapshotPruneResult Empty { get; } = new(0, 0);
    }

    private sealed record PowerspikeCollapseResult(int CollapsedGroups, int DeletedShards)
    {
        public static PowerspikeCollapseResult Empty { get; } = new(0, 0);
    }

    private sealed record DeletionResult(int DeletedMatches, int DeletedParticipants)
    {
        public static DeletionResult Empty { get; } = new(0, 0);
    }

    private sealed record AggregateDeletionResult(
        int DeletedScopes,
        int DeletedMatchupStats,
        int DeletedPowerspikeCurveStats,
        int DeletedPowerspikeEventStats,
        int DeletedSynergyStats,
        int DeletedBanStats)
    {
        public static AggregateDeletionResult Empty { get; } = new(0, 0, 0, 0, 0, 0);

        public int TotalDeleted
            => DeletedScopes
                + DeletedMatchupStats
                + DeletedPowerspikeCurveStats
                + DeletedPowerspikeEventStats
                + DeletedSynergyStats
                + DeletedBanStats;
    }

    private sealed record RetentionPlan(
        int RetainedPatchCount,
        int QueueId,
        IReadOnlyDictionary<string, HashSet<string>> RetainedPatchesByPlatform,
        IReadOnlyList<string> DeletableMatchIds);
}
