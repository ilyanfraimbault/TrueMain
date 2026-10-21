using Core.Options;
using Ingestor.Options;
using Ingestor.Processes.Components.PatternAggregation;
using Ingestor.Processes.Summaries;
using Microsoft.Extensions.Options;

namespace Ingestor.Processes;

/// <summary>
/// Builds the champion pattern aggregates — <c>champion_aggregate_scopes</c> and their
/// <c>champion_aggregate_patterns</c> — incrementally (#1367). It used to rebuild every
/// scope of the retained window on every run, so a run cost the whole patch and grew with
/// it: about an hour on production and two on preprod late in a patch.
///
/// <para>
/// <b>The unit is the slice</b> (<see cref="AggregateSliceKey"/>): one account's games on
/// one champion, patch and platform. A scope adds the position and the elo bracket, both
/// read off the slice's own games, so a slice rebuilt from its games alone is complete.
/// A run takes the pending matches (<c>matches.PatternAggregated = false</c>) a step at a
/// time, rebuilds every slice a tracked participant of theirs belongs to, then flags them.
/// Steady state that is what was ingested since the previous run, at a few games per slice.
/// </para>
///
/// <para>
/// <b>Not fold-once.</b> The other per-match folds add counters and can never take a game
/// back out, so they read a participant row once and live with it. A slice is replaced
/// whole, so this one rebuilds instead: whatever rewrites a column the slice reads sends
/// the match back — the rank stamp, the lane fix and the role-bound backfill
/// (<c>PatternAggregationInvalidation</c>), and the main analysis when a player's
/// main-ness flips (<c>IMatchRepository.InvalidatePatternAggregationAsync</c>). The elo
/// bracket is the participant's stamp, no longer re-derived from the snapshots each run,
/// so a scope and the live panels bucket a game the same way.
/// </para>
///
/// <para>
/// Old patches stay frozen exactly as before (#466): a pending match outside the retained
/// window rebuilds nothing and is flagged. <c>PatternAggregation:RebuildAll</c> sends the
/// whole window back for one run when the slice shape changes.
/// </para>
/// </summary>
public sealed class ChampionPatternAggregationProcess(
    ILogger<ChampionPatternAggregationProcess> logger,
    IOptions<MainAnalysisOptions> analysisOptions,
    IOptions<MatchDataRetentionOptions> retentionOptions,
    IOptions<PatternAggregationOptions> options,
    ChampionPatternSourceRowReader sourceRowReader,
    ChampionPatternAggregateBuilder aggregateBuilder,
    ChampionPatternAggregatePersister aggregatePersister,
    TimeProvider timeProvider) : IIngestorProcess
{
    public string Name => "ChampionPatternAggregation";

    public async Task<IProcessRunSummary?> RunCoreAsync(CancellationToken ct)
    {
        var queueId = (int)analysisOptions.Value.QueueId;

        // Off unless ops turns it on: widening to the non-main population is ~4.3x the
        // rows on production, on the process that once OOM-killed the VPS (#1346, #601).
        var includeNonMains = analysisOptions.Value.AggregateNonMainPopulation;

        // One snapshot of the rebuildable window for the whole run (#1549): every slice
        // rebuilt below comes from inside it, and a patch that enters it mid-run is picked
        // up by the next run.
        var livePatchKeys = await sourceRowReader.LoadLivePatchKeysAsync(
            queueId, retentionOptions.Value.RetainedPatchCount, ct);

        if (options.Value.RebuildAll)
        {
            var requeued = await sourceRowReader.RequeueWindowAsync(queueId, livePatchKeys, ct);
            logger.LogWarning(
                "PatternAggregation:RebuildAll is on: {Requeued} match(es) of the retained window sent back. "
                + "Turn it off once this run has completed.",
                requeued);
        }

        // The light per-patch record (#1956) is folded with every slice replaced below, which
        // is only exact over a record that already holds the scopes being replaced: the
        // first run after it ships fills it from every scope present.
        var backfilled = await aggregatePersister.BackfillPatchStatsIfEmptyAsync(
            timeProvider.GetUtcNow().UtcDateTime, ct);
        if (backfilled > 0)
        {
            logger.LogInformation(
                "Light per-patch record backfilled from the existing scopes: {Rows} row(s).", backfilled);
        }

        // Read once, before any slice is rebuilt. A slice is rebuilt at most once per run
        // and from every game present at that moment, which is complete for a match only if
        // the match was already there — true of every id in this list, not of a match the
        // fetch lane inserts later, which simply waits for the next run.
        var matchIds = await sourceRowReader.LoadPendingMatchIdsAsync(queueId, ct);
        if (matchIds.Count == 0)
        {
            return new ChampionPatternNoWorkSummary("No pending match for the champion pattern aggregation.", 0);
        }

        var aggregatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var totalSourceRows = 0;
        var totalScopes = 0;
        var totalPatterns = 0;
        var champions = new HashSet<int>();
        var gameVersions = new HashSet<string>(StringComparer.Ordinal);
        var rebuilt = new HashSet<AggregateSliceKey>();

        // A step at a time: rebuild what a step's matches touch, then flag them, so a run cut
        // short by a deploy keeps what it did. A slice an earlier step already rebuilt is not
        // rebuilt again — its games were all read then.
        foreach (var step in matchIds.Chunk(options.Value.MatchesPerStep))
        {
            ct.ThrowIfCancellationRequested();

            var stepSlices = await sourceRowReader.LoadSlicesAsync(step, livePatchKeys, ct);
            stepSlices.ExceptWith(rebuilt);

            // Batched by account: a batch's working set is those accounts' games on the
            // champions being rebuilt, never a whole champion's patch. Each batch commits on
            // its own, so a crash leaves every slice internally consistent.
            var batches = stepSlices
                .GroupBy(slice => slice.RiotAccountId)
                .OrderBy(group => group.Key)
                .Chunk(options.Value.AccountsPerBatch);

            foreach (var accounts in batches)
            {
                var slices = accounts.SelectMany(account => account).ToList();
                var sourceRows = await sourceRowReader.LoadSourceRowsAsync(queueId, slices, includeNonMains, ct);
                var result = await aggregateBuilder.BuildAggregatesAsync(sourceRows, aggregatedAtUtc, ct);
                await aggregatePersister.ReplaceSlicesAsync(
                    queueId, slices, result.Scopes, result.Patterns, aggregatedAtUtc, ct);

                totalSourceRows += result.SourceRowCount;
                totalScopes += result.Scopes.Count;
                totalPatterns += result.Patterns.Count;
                foreach (var scope in result.Scopes)
                {
                    champions.Add(scope.ChampionId);
                    gameVersions.Add(scope.GameVersion);
                }
            }

            rebuilt.UnionWith(stepSlices);
            await sourceRowReader.MarkAggregatedAsync(step, ct);
        }

        logger.LogInformation(
            "Champion pattern aggregation summary: matches={Matches}, slices={Slices}, sourceRows={SourceRows}, "
            + "scopes={ScopeCount}, patterns={PatternCount}.",
            matchIds.Count,
            rebuilt.Count,
            totalSourceRows,
            totalScopes,
            totalPatterns);

        return new ChampionPatternAggregationSummary(
            totalSourceRows,
            totalScopes,
            totalPatterns,
            gameVersions.Count,
            champions.Count,
            matchIds.Count,
            rebuilt.Count);
    }
}
