using System.Collections.ObjectModel;

namespace Ingestor.Options;

/// <summary>
/// Expands a configured <see cref="JobMode"/> into the ordered list of steps the
/// worker runs. Every mode except <see cref="JobMode.Full"/> is a single-process
/// mode and maps to itself; <see cref="JobMode.Full"/> is the whole pipeline in
/// dependency order. Each step is the DI key the matching
/// <c>IIngestorProcess</c> is registered under, so a step that has no
/// registration is a compile-time-visible enum value rather than a string typo.
/// </summary>
public static class JobModeSequence
{
    // Wrapped rather than returned as the bare array: For() hands this instance
    // straight back to every caller, and a JobMode[] would let one of them cast
    // the IReadOnlyList back and reorder the shared pipeline.
    private static readonly ReadOnlyCollection<JobMode> FullPipeline = Array.AsReadOnly<JobMode>(
    [
        // Reads the ladders themselves to refresh the rank of accounts we already track
        // (#1312), before Discovery. Cheap and broad where AccountRefresh is expensive and
        // narrow: the apex tiers cost one call each, and everything below Master is swept
        // incrementally under its own request budget. Running it first means the snapshots
        // the later EloBracketEnrichment step reads are this cycle's, not the previous one's.
        JobMode.LadderSyncOnly,
        JobMode.DiscoveryOnly,
        // ManualSeed runs right after Discovery and before Scoring: it queues its
        // candidates directly (skipping the competitive top-N ScoringProcess), so
        // a seeded account is picked up by the same downstream MatchIngestion ->
        // MainAnalysis pass in this run.
        JobMode.ManualSeedOnly,
        // Harvest generates candidates from orphan match_participants rows at
        // near-zero Riot cost (#485). It runs before Scoring so harvested
        // candidates compete in the same per-platform top-N as ladder/manual ones.
        JobMode.HarvestOnly,
        JobMode.ScoringOnly,
        // Retires mains that stopped playing (#900) BEFORE the claim, so the batch
        // that follows spends its match-v5 budget on players who still play instead
        // of re-reading accounts that will come back empty.
        JobMode.MainActivityOnly,
        JobMode.MatchIngestionOnly,
        // Backfills any pre-existing "Missing team position" gap left by upstream
        // Riot data before the champion aggregations read TeamPosition.
        // RiotMatchMapper already self-heals newly-ingested matches, so
        // steady-state this only drains the pre-existing backlog.
        JobMode.TeamPositionCorrectionOnly,
        JobMode.MainAnalysisOnly,
        // Stamps match_participants.elo_bracket from the nearest rank snapshot
        // BEFORE the champion aggregations, so they (and the live panel reads) can
        // filter by rank. Uses prior-cycle snapshots.
        JobMode.EloBracketEnrichmentOnly,
        // Collapses the permutation-duplicate rune pages the old click-order
        // storage created (#911) BEFORE the aggregation reads the dimension, so a
        // pass never aggregates into a table it is about to rewrite. Drains once
        // and is a no-op thereafter.
        JobMode.RunePageDeduplicationOnly,
        JobMode.PatternAggregationOnly,
        JobMode.MatchupLeadAggregationOnly,
        // Judges each match's lane outcome from the 15-minute snapshots and folds it
        // into the lane counters on the same champion_matchup_stats rows (#919).
        // Immediately after the matchup fold, over the same matches, so the row its
        // upsert targets already exists and both sides describe one cohort.
        JobMode.LaneOutcomeAggregationOnly,
        // Same incremental one-fold-per-match shape as the matchup step, over the
        // same participant rows but pairing teammates instead of lane opponents
        // (#922). Independent of it — it has its own pending flag — so the order
        // between the two is arbitrary; kept adjacent because they read the same
        // slice of match_participants and benefit from a warm cache.
        JobMode.SynergyAggregationOnly,
        // Folds each match's champion-select bans into champion_ban_stats (#920).
        // Must run after EloBracketEnrichment, whose stamping decides which elo
        // bands a match is counted in — a match folded before its participants are
        // stamped lands in the ALL band only, and the fold is one-shot.
        JobMode.BanAggregationOnly,
        // Folds each newly-ingested match into the powerspike aggregates (#694)
        // while its dense per-minute snapshots still exist, so MatchDataRetention
        // can then prune them to the canonical marks.
        JobMode.PowerspikeAggregationOnly,
        JobMode.AccountRefreshOnly,
        JobMode.MatchDataRetentionOnly,
        // Records the day's storage footprint (#925). Deliberately last: it must
        // measure the steady-state size AFTER retention's deletions, not the peak
        // before them, or the forecast would predict an exhaustion retention is
        // already preventing.
        JobMode.StorageSnapshotOnly
    ]);

    /// <summary>
    /// Returns the ordered steps to run for <paramref name="mode"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is not a declared <see cref="JobMode"/>. Failing here is
    /// deliberate: the previous string-based mapping fell back to the full
    /// pipeline for any unmatched value, so a misconfigured mode silently ran
    /// everything instead of surfacing the mistake.
    /// </exception>
    public static IReadOnlyList<JobMode> For(JobMode mode)
    {
        if (mode == JobMode.Full)
        {
            return FullPipeline;
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                mode,
                $"Unknown {nameof(JobMode)} value.");
        }

        return [mode];
    }
}
