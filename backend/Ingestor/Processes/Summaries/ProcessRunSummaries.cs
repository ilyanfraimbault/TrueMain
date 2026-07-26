namespace Ingestor.Processes.Summaries;

// Persisted shapes — DO NOT rename a property without a migration plan.
//
// These records replace the anonymous types the processes used to return. The
// summaries are stored in process_runs.summary and rendered as-is by the admin
// portal (ProcessSummaryView walks whatever keys the JSON carries), so the
// emitted property names are a persisted contract: the anonymous members were
// declared in camelCase and serialized verbatim (no naming policy), and
// ProcessRunSummaryJson pins JsonNamingPolicy.CamelCase so these PascalCase
// properties emit byte-identical JSON. ProcessRunSummaryJsonTests locks that
// down by comparing each record against the original anonymous shape.

/// <summary>Nothing to do: no input selected for this run.</summary>
public sealed record NoWorkSummary(string Reason, int Selected) : IProcessRunSummary;

/// <summary>The run was deliberately skipped (e.g. a cadence guard).</summary>
public sealed record SkippedSummary(string Reason, bool Skipped) : IProcessRunSummary;

/// <summary>Harvest had nothing to do.</summary>
public sealed record HarvestNoWorkSummary(string Reason, int CandidatesInserted) : IProcessRunSummary;

/// <summary>Manual seed had nothing to claim.</summary>
public sealed record ManualSeedNoWorkSummary(string Reason, int Claimed) : IProcessRunSummary;

/// <summary>Champion pattern aggregation had no champion to aggregate.</summary>
public sealed record ChampionPatternNoWorkSummary(string Reason, int Patterns) : IProcessRunSummary;

/// <summary>Per-platform scoring outcome.</summary>
public sealed record ScoringPlatformSummary(string Platform, int Scored, int Queued);

/// <summary>Scoring outcome, one entry per platform that had scored candidates.</summary>
public sealed record ScoringSummary(IReadOnlyList<ScoringPlatformSummary> Platforms) : IProcessRunSummary;

/// <summary>
/// Per-platform discovery outcome. <see cref="Error"/> is null for platforms that
/// completed and carries the failure message otherwise, so a partially failed run
/// says which platform failed and why.
/// </summary>
public sealed record DiscoveryPlatformSummary(
    string Platform,
    int AccountsProcessed,
    int NewAccounts,
    int CandidatesInserted,
    int CandidatesUpdated,
    int RankSnapshotsInserted,
    int RankSnapshotsUnchanged,
    string? Error);

/// <summary>Discovery outcome, one entry per attempted platform.</summary>
public sealed record DiscoverySummary(IReadOnlyList<DiscoveryPlatformSummary> Platforms) : IProcessRunSummary;

/// <summary>Per-platform match ingestion outcome.</summary>
public sealed record MatchIngestionPlatformSummary(
    string Platform,
    int AccountsProcessed,
    int MatchesInserted,
    int MatchesSkipped,
    int TimelinesUpdated);

/// <summary>Match ingestion outcome, with a per-platform breakdown.</summary>
public sealed record MatchIngestionSummary(
    int AccountsProcessed,
    int MatchesInserted,
    int MatchesSkipped,
    int TimelinesUpdated,
    int Errors,
    IReadOnlyList<MatchIngestionPlatformSummary> ByPlatform) : IProcessRunSummary;

/// <summary>Manual seed outcome for the claimed batch.</summary>
public sealed record ManualSeedSummary(
    int Claimed,
    int Ingested,
    int NotFound,
    int Failed,
    int CandidatesQueued) : IProcessRunSummary;

/// <summary>
/// Participant harvest outcome, including the anti-starvation coverage split (#495)
/// between newly-discovered and already-known (puuid, champion) pairs.
/// </summary>
public sealed record HarvestSummary(
    int CandidatesInserted,
    int CandidatesUpdated,
    int AccountsCreated,
    int EligibleNew,
    int SelectedNew,
    int EligibleKnown,
    int SelectedKnown,
    bool BudgetExhausted) : IProcessRunSummary;

/// <summary>Account refresh outcome, split by profile and rank sub-step.</summary>
public sealed record AccountRefreshSummary(
    int Selected,
    int ProfileUpdated,
    int ProfileRecovered,
    int ProfileInvalidated,
    int ProfileSkipped,
    int ProfileFailed,
    int RankInserted,
    int RankUnchanged,
    int RankSkippedUnranked,
    int RankSkippedFresh,
    int RankFailed) : IProcessRunSummary;

/// <summary>Main analysis outcome.</summary>
public sealed record MainAnalysisSummary(
    int AccountsProcessed,
    int StatsUpserted,
    int StatsRemoved,
    int DemotedAccounts) : IProcessRunSummary;

/// <summary>Champion pattern aggregation outcome.</summary>
public sealed record ChampionPatternAggregationSummary(
    int SourceRows,
    int Scopes,
    int Patterns,
    int GameVersions,
    int Champions) : IProcessRunSummary;

/// <summary>Elo-bracket enrichment outcome.</summary>
public sealed record EloBracketEnrichmentSummary(int Stamped, int Deferred, int Batches) : IProcessRunSummary;

/// <summary>Team position correction outcome.</summary>
public sealed record TeamPositionCorrectionSummary(int CorrectedParticipants, int InspectedTeams) : IProcessRunSummary;

/// <summary>Batched match aggregation outcome (matchup/lead and powerspike).</summary>
public sealed record MatchAggregationSummary(int Matches, int Batches) : IProcessRunSummary;

/// <summary>The patches retention kept for one platform.</summary>
public sealed record RetainedPatchesSummary(string PlatformId, IReadOnlyList<string> Patches);

/// <summary>Match data retention outcome across every deletion and pruning arm.</summary>
public sealed record MatchDataRetentionSummary(
    int RetainedPatchCount,
    int QueueId,
    int DeletedMatches,
    int DeletedParticipants,
    int DeletedNonRankedMatches,
    int PrunedCandidates,
    int PrunedSnapshotMatches,
    int DeletedIntermediateSnapshots,
    int DeletedAggregateScopes,
    int DeletedMatchupStats,
    int DeletedPowerspikeCurveStats,
    int DeletedPowerspikeEventStats,
    int PrunedSubFloorPowerspikeEvents,
    IReadOnlyList<RetainedPatchesSummary> RetainedPatchesByPlatform) : IProcessRunSummary;
