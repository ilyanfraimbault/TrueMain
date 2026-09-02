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
/// <para>
/// <see cref="ProfileCallsSkipped"/> / <see cref="MasteryCallsSkipped"/> (#1358) are the
/// summoner-v4 and champion-mastery calls the run did <em>not</em> make because the stored row
/// was already fresh — the ladder entry carries the PUUID, so those calls would have returned
/// nothing the database did not already have. Appended after <see cref="Error"/> so the
/// pre-existing keys keep their wire order.
/// </para>
/// </summary>
public sealed record DiscoveryPlatformSummary(
    string Platform,
    int AccountsProcessed,
    int NewAccounts,
    int CandidatesInserted,
    int CandidatesUpdated,
    int RankSnapshotsInserted,
    int RankSnapshotsUpdated,
    int RankSnapshotsUnchanged,
    string? Error,
    int ProfileCallsSkipped,
    int MasteryCallsSkipped);

/// <summary>Discovery outcome, one entry per attempted platform.</summary>
public sealed record DiscoverySummary(IReadOnlyList<DiscoveryPlatformSummary> Platforms) : IProcessRunSummary;

/// <summary>
/// Per-platform match ingestion outcome. <see cref="MatchesSkipped"/> is the healthy skip —
/// ids we already had stored, which cost no per-match call — while
/// <see cref="MatchesSkippedWrongQueue"/> (#1358) counts matches fetched and then discarded for
/// being off-queue, i.e. calls that stored nothing. Appended last so the pre-existing keys keep
/// their wire order.
/// </summary>
public sealed record MatchIngestionPlatformSummary(
    string Platform,
    int AccountsProcessed,
    int MatchesInserted,
    int MatchesSkipped,
    int TimelinesUpdated,
    int MatchesSkippedWrongQueue);

/// <summary>
/// Match ingestion outcome, with a per-platform breakdown.
/// <see cref="AccountsValidated"/> (#1024) is the candidate funnel's exit: accounts whose
/// candidates cleared ingestion and moved Processing → Validated. It is lower than
/// <see cref="AccountsProcessed"/> whenever a claimed account had nothing left to promote,
/// and it is the only record of a validation — the transition itself leaves no trace in
/// <c>main_candidates</c> once retention prunes the row. Appended after <see cref="Errors"/>
/// so the pre-existing keys keep their wire order.
/// <para>
/// <see cref="ExpiredCandidatesReleased"/> / <see cref="ExpiredClaimsReleased"/> (#1344) are
/// what the run reaped before claiming: rows a previous run died holding. Non-zero means a
/// run died, not that this one misbehaved — a healthy steady state reports zeroes.
/// </para>
/// <para>
/// <see cref="MatchesSkippedWrongQueue"/> (#1358) splits the calls that stored nothing out of
/// <see cref="MatchesSkipped"/>, which now means "already stored" only. Since the ids call sends
/// <c>queue</c>, a non-zero value means Riot returned an off-queue id anyway — visible instead of
/// buried inside a number that is normally large.
/// </para>
/// <para>
/// <see cref="AccountsWithoutNewMatches"/> (#1360) is how the claim's ordering is judged: an
/// account whose visit produced nothing spent a match-ids call and a batch slot to learn that
/// the player had not played. Ordering by age alone made that the common case; ordering by
/// games actually played since the last visit should drive it toward zero. Appended last so
/// the pre-existing keys keep their wire order.
/// </para>
/// </summary>
public sealed record MatchIngestionSummary(
    int AccountsProcessed,
    int MatchesInserted,
    int MatchesSkipped,
    int TimelinesUpdated,
    int Errors,
    int AccountsValidated,
    int ExpiredCandidatesReleased,
    int ExpiredClaimsReleased,
    int MatchesSkippedWrongQueue,
    IReadOnlyList<MatchIngestionPlatformSummary> ByPlatform,
    int AccountsWithoutNewMatches = 0) : IProcessRunSummary;

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

/// <summary>How many ladder entries one tier contributed to a sync run.</summary>
public sealed record LadderSyncTierSummary(string Tier, int EntriesFetched);

/// <summary>
/// Ladder sync outcome (#1312). <see cref="ApexCalls"/> counts the whole-tier reads, which run
/// every cycle, and <see cref="PagedCalls"/> the budgeted per-division pages. The ratio of
/// <see cref="AccountsMatched"/> to <see cref="EntriesFetched"/> — broken down by
/// <see cref="Tiers"/> — is what says whether a swept tier earns its page cost.
/// </summary>
public sealed record LadderSyncSummary(
    int ApexCalls,
    int PagedCalls,
    int FailedCalls,
    int EntriesFetched,
    int AccountsMatched,
    int RankInserted,
    int RankUpdated,
    int RankUnchanged,
    IReadOnlyList<LadderSyncTierSummary> Tiers) : IProcessRunSummary;

/// <summary>
/// Account refresh outcome, split by profile and rank sub-step.
/// <see cref="ProfileSkippedFresh"/> (#1358) is the account-v1 call not made because the stored
/// profile is younger than <c>AccountRefresh:ProfileSyncFreshness</c> — the profile mirror of the
/// existing <see cref="RankSkippedFresh"/>. Appended last so the pre-existing keys keep their
/// wire order.
/// </summary>
public sealed record AccountRefreshSummary(
    int Selected,
    int ProfileUpdated,
    int ProfileRecovered,
    int ProfileInvalidated,
    int ProfileSkipped,
    int ProfileFailed,
    int RankInserted,
    int RankUpdated,
    int RankUnchanged,
    int RankSkippedUnranked,
    int RankSkippedFresh,
    int RankFailed,
    int ProfileSkippedFresh) : IProcessRunSummary;

/// <summary>Main analysis outcome.</summary>
public sealed record MainAnalysisSummary(
    int AccountsProcessed,
    int StatsUpserted,
    int StatsRemoved,
    int DemotedAccounts) : IProcessRunSummary;

/// <summary>Champion-mastery activity check outcome (#900).</summary>
public sealed record MainActivitySummary(
    int AccountsChecked,
    int MainsDeactivated,
    int MainsReactivated,
    int AccountsFailed,
    int AccountsSkipped) : IProcessRunSummary;

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

/// <summary>
/// Champion synergy aggregation outcome (#922). Carries the two upsert counts on
/// top of the shared match/batch pair so the admin's aggregation page can tell a
/// run that folded matches but wrote nothing (every match off-position or
/// untracked) from one that had no matches to fold at all.
/// </summary>
public sealed record SynergyAggregationSummary(
    int Matches,
    int Batches,
    int PairRows,
    int BaselineRows) : IProcessRunSummary;

/// <summary>
/// Champion ban aggregation outcome (#920). <see cref="ScopeRows"/> counts the
/// (patch, elo band) denominators touched and <see cref="BanRows"/> the champion
/// counts, so a run whose matches all folded into the ALL band alone — every
/// participant still awaiting elo enrichment — is visible as a scope count of one.
/// </summary>
public sealed record BanAggregationSummary(
    int Matches,
    int Batches,
    int BanRows,
    int ScopeRows) : IProcessRunSummary;

/// <summary>
/// Daily storage snapshot outcome (#925). <see cref="Written"/> is 0 rather than
/// <see cref="Tables"/> when Mongo is unconfigured, which is how an environment with
/// no metrics store shows up on the admin process page — a completed run that
/// persisted nothing, not a failure.
/// </summary>
/// <remarks>
/// The Mongo counters (#1023) are the engine's own footprint, measured through
/// <c>dbStats</c> / <c>$collStats</c>. They are 0 when Mongo is unconfigured — the
/// same condition that zeroes <see cref="Written"/>, since that is also where the
/// readings would have been stored.
/// </remarks>
public sealed record StorageSnapshotSummary(
    int Tables,
    int Written,
    long DatabaseBytes,
    int MongoCollections,
    int MongoWritten,
    long MongoBytes) : IProcessRunSummary;

/// <summary>
/// Rune-page deduplication outcome (#911). The counters separate the two ways a
/// pattern row can move: <see cref="RepointedPatterns"/> simply changed page,
/// <see cref="FoldedPatterns"/> had its games/wins added into an existing row and was
/// deleted — the second number is the split this bug was causing, being undone.
/// <see cref="NormalizedPages"/> counts rows that were never duplicated but still held
/// the player's perk order.
/// </summary>
public sealed record RunePageDeduplicationSummary(
    int Groups,
    int DeletedPages,
    int RepointedPatterns,
    int FoldedPatterns,
    int NormalizedPages,
    int Batches) : IProcessRunSummary;

/// <summary>
/// Lane-outcome aggregation outcome (#919). <see cref="JudgedLanes"/> is the count of
/// lanes that could actually be called — both participants had a 15-minute snapshot —
/// so comparing it with <see cref="Matches"/> shows how much of the pool has no
/// timeline. <see cref="GoldLeadThreshold"/> is recorded because it defines what the
/// stored counters mean, and old rows keep the threshold in force when they were folded.
/// </summary>
public sealed record LaneOutcomeAggregationSummary(
    int Matches,
    int Batches,
    int JudgedLanes,
    int Rows,
    int GoldLeadThreshold) : IProcessRunSummary;

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
    // Pair rows and the baselines they are read against, summed (#922) — they are
    // deleted together in one transaction, so one counter describes both.
    int DeletedSynergyStats,
    // Ban counts and the match totals they are divided by, summed (#920) — deleted
    // together in one transaction, so one counter describes both.
    int DeletedBanStats,
    int PrunedSubFloorPowerspikeEvents,
    // Per-opponent powerspike shards (#957) rolled back into one opponent-less row
    // once their patch froze, and how many rows that produced. Runs before the
    // sub-floor prune above, which must see the rolled-up games rather than shards.
    int CollapsedPowerspikeOpponentShards,
    int CollapsedPowerspikeOpponentGroups,
    IReadOnlyList<RetainedPatchesSummary> RetainedPatchesByPlatform) : IProcessRunSummary;
