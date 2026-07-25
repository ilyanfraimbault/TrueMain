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
        JobMode.PatternAggregationOnly,
        JobMode.MatchupLeadAggregationOnly,
        // Folds each newly-ingested match into the powerspike aggregates (#694)
        // while its dense per-minute snapshots still exist, so MatchDataRetention
        // can then prune them to the canonical marks.
        JobMode.PowerspikeAggregationOnly,
        JobMode.AccountRefreshOnly,
        JobMode.MatchDataRetentionOnly
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
