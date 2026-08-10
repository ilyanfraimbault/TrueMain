namespace TrueMain.ReadModels.Ops;

/// <summary>
/// Everything the pipeline knows about one Riot ID, for the admin account
/// explorer (#1032): identity, tracking/lease state, the candidate funnel, the
/// main-champion rows and the rank history, in one read.
/// <para>
/// The endpoint answers "why does this player not show up on the site?", so its
/// contract is that <em>no state is ever inferred from an absent row without
/// saying so</em>. An unknown Riot ID is a 200 with <see cref="State"/> =
/// <c>NeverDiscovered</c>, never a 404 — "we have never seen this account" is an
/// answer, not an error. Counts each carry the population they count, and the
/// retention-bounded ones carry the window they were measured over.
/// </para>
/// <para>
/// Read-only, and deliberately database-only: the API holds no Riot client, so
/// this read cannot (and does not claim to) tell "never discovered" apart from
/// "this Riot ID does not exist at Riot".
/// </para>
/// </summary>
public sealed record AccountExplorerReadModel
{
    /// <summary>The Riot ID and region the answer was resolved for, echoed back.</summary>
    public AccountExplorerQueryReadModel Query { get; init; } = new();

    /// <summary>
    /// One-word verdict, resolved first-match-wins so a player always lands in
    /// exactly one state. See <see cref="AccountPipelineState"/> for the ladder
    /// and what each value means. The booleans behind it stay exposed in the
    /// sections below — the state is a headline, not the source of truth.
    /// </summary>
    public string State { get; init; } = string.Empty;

    /// <summary>
    /// The state in a sentence, built server-side so every consumer explains a
    /// given state identically.
    /// </summary>
    public string StateDetail { get; init; } = string.Empty;

    /// <summary>Null when no <c>riot_accounts</c> row matches the Riot ID.</summary>
    public AccountExplorerIdentityReadModel? Identity { get; init; }

    /// <summary>
    /// The other accounts carrying the same Riot ID. <c>(GameName, TagLine,
    /// PlatformId)</c> is deliberately <em>not</em> unique — Riot IDs are
    /// recyclable and can collide across regions — so the resolver picks the
    /// most recently active row and lists the rest here rather than arbitrating
    /// in silence. Empty in the normal single-account case.
    /// </summary>
    public IReadOnlyList<AccountExplorerAccountRefReadModel> OtherAccountsWithSameRiotId { get; init; } = [];

    /// <summary>Null when there is no account row to track.</summary>
    public AccountExplorerTrackingReadModel? Tracking { get; init; }

    /// <summary>Null when there is no account row to count games for.</summary>
    public AccountExplorerMatchesIngestedReadModel? MatchesIngested { get; init; }

    /// <summary>
    /// The account's <c>main_candidates</c> rows, highest score first. Always
    /// empty when <see cref="Identity"/> is null: candidates are keyed on
    /// <c>(PlatformId, Puuid)</c> and carry no Riot ID of their own, so a
    /// candidate whose account has not been upserted yet is unreachable from a
    /// Riot ID search.
    /// </summary>
    public IReadOnlyList<AccountExplorerCandidateReadModel> Candidates { get; init; } = [];

    /// <summary>
    /// The manual "add a main" request behind this account, if any — the only
    /// reliable manual-seed signal (see
    /// <see cref="AccountExplorerCandidateReadModel.Source"/>). Matched on the
    /// resolved PUUID + platform when the account exists, and on the Riot ID
    /// text when it does not — which is how a seed that never resolved is still
    /// visible.
    /// </summary>
    public SeedRequestReadModel? SeedRequest { get; init; }

    /// <summary>The <c>main_champion_stats</c> rows plus the thresholds that judged them.</summary>
    public AccountExplorerMainsReadModel Mains { get; init; } = new();

    /// <summary>
    /// Rank snapshots, most recent first, capped at
    /// <c>AccountExplorerQueryService.RankSnapshotCap</c>. At most one row per
    /// UTC day, solo queue only, and never pruned by retention — so this is the
    /// one series whose gaps really are gaps in play, not in storage.
    /// </summary>
    public IReadOnlyList<AccountExplorerRankSnapshotReadModel> RankSnapshots { get; init; } = [];
}

/// <summary>
/// The verdict ladder, resolved in declaration order — the first matching state
/// wins. Serialized as its name.
/// </summary>
public enum AccountPipelineState
{
    /// <summary>
    /// No <c>riot_accounts</c> row and no seed request for this Riot ID. The
    /// pipeline has never encountered it. Says nothing about whether the Riot ID
    /// exists — this read never calls Riot.
    /// </summary>
    NeverDiscovered,

    /// <summary>
    /// No account row, but a manual seed request exists. Either it has not been
    /// drained yet (Pending/Resolving) or it failed to resolve — the request's
    /// own status and error say which.
    /// </summary>
    SeedRequestedOnly,

    /// <summary>
    /// <c>RiotAccount.Status = Invalid</c>: account-v1 404s on the PUUID and
    /// <c>AccountRefresh</c> could not recover it by Riot ID. The row is kept for
    /// history but excluded from every refresh and ingest selection, so nothing
    /// downstream will ever move again.
    /// </summary>
    Invalidated,

    /// <summary>
    /// In the match-ingestion population: a <c>Queued</c> candidate, an active
    /// main, or both — the two membership arms of the ingest claim.
    /// </summary>
    Tracked,

    /// <summary>
    /// Was a main and no longer is: at least one <c>IsMain</c> row exists and
    /// none of them is still <c>IsActive</c>. <c>MainActivity</c> deactivates,
    /// never deletes (#900).
    /// </summary>
    Retired,

    /// <summary>
    /// <c>MainAnalysis</c> has written rows for this account but promoted none of
    /// them past the adaptive <c>IsMain</c> floor (#407) — the account is
    /// analysed, just not a main of anything.
    /// </summary>
    NotAMain,

    /// <summary>
    /// Known to the candidate funnel but never analysed: candidate rows exist,
    /// <c>main_champion_stats</c> is empty. The candidates' own statuses say how
    /// far up the funnel they got.
    /// </summary>
    CandidateOnly,

    /// <summary>
    /// The account exists and nothing else has happened to it: no candidate, no
    /// main row, not in the ingest population.
    /// </summary>
    Discovered
}

/// <summary>The request as the service resolved it.</summary>
public sealed record AccountExplorerQueryReadModel
{
    public string GameName { get; init; } = string.Empty;

    public string TagLine { get; init; } = string.Empty;

    /// <summary>The requested platform id (e.g. "EUW1"), or null when the search was region-wide.</summary>
    public string? Region { get; init; }
}

/// <summary>
/// The resolved account's identity and the per-process freshness stamps that say
/// when each half of the pipeline last touched it.
/// </summary>
public sealed record AccountExplorerIdentityReadModel
{
    public Guid RiotAccountId { get; init; }

    public string Puuid { get; init; } = string.Empty;

    public string GameName { get; init; } = string.Empty;

    public string? TagLine { get; init; }

    public string PlatformId { get; init; } = string.Empty;

    public int ProfileIconId { get; init; }

    public int SummonerLevel { get; init; }

    /// <summary>The <c>RiotAccountStatus</c> name: "Active" or "Invalid".</summary>
    public string Status { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; init; }

    /// <summary>Last successful account-v1 identity resolution by <c>AccountRefresh</c>.</summary>
    public DateTime? LastProfileSyncAtUtc { get; init; }

    /// <summary>
    /// Last successful league-v4 read. Stamped even when the rank was unchanged
    /// (no snapshot row written), so it is a freshness stamp, not a change log.
    /// </summary>
    public DateTime? LastRankSyncAtUtc { get; init; }

    /// <summary>
    /// Last time <c>MainAnalysis</c> ran on this account. Can be newer than every
    /// row's <c>CalculatedAtUtc</c> — see
    /// <see cref="AccountExplorerMainRowReadModel.AnalysisSkipped"/>.
    /// </summary>
    public DateTime? LastMainCalcAtUtc { get; init; }

    /// <summary>
    /// Last <em>successful</em> champion-mastery check by <c>MainActivity</c>. A
    /// failed lookup leaves this untouched on purpose, which is what makes it a
    /// usable "the retirement below was confirmed" marker.
    /// </summary>
    public DateTime? LastActivityCheckAtUtc { get; init; }

    /// <summary>Last completed match-ingestion pass over this account.</summary>
    public DateTime? LastMatchIngestAtUtc { get; init; }

    /// <summary>
    /// Denormalised rank sort key (tier/division/LP) from the latest snapshot.
    /// Null when the account has never been seen ranked — not zero.
    /// </summary>
    public int? RankScore { get; init; }
}

/// <summary>One of the other accounts sharing this Riot ID.</summary>
public sealed record AccountExplorerAccountRefReadModel
{
    public Guid RiotAccountId { get; init; }

    public string Puuid { get; init; } = string.Empty;

    public string PlatformId { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public DateTime? LastMatchIngestAtUtc { get; init; }
}

/// <summary>
/// Whether the account is in the match-ingestion population, and where its lease
/// currently stands.
/// <para>
/// Every threshold that would turn these numbers into a verdict — the claim
/// lease, the inactivity window, the retained patch count — is Ingestor
/// configuration the API cannot see. So this section reports timestamps and ages
/// and stops there; it never claims a lease is "stale".
/// </para>
/// </summary>
public sealed record AccountExplorerTrackingReadModel
{
    /// <summary>
    /// True when the account satisfies either membership arm of the ingest claim.
    /// There is no <c>IsTracked</c> column — this is derived, exactly as
    /// <c>ClaimAccountsForMatchIngestAtomicallyAsync</c> derives it.
    /// </summary>
    public bool IsTracked { get; init; }

    /// <summary>
    /// Which arm: "EstablishedMain", "QueuedCandidate", "Both", or null when
    /// neither — i.e. the account is never selected for ingestion.
    /// </summary>
    public string? TrackedVia { get; init; }

    /// <summary>True when an <c>IsMain</c> + <c>IsActive</c> row exists.</summary>
    public bool HasActiveMain { get; init; }

    /// <summary>True when a candidate sits at <c>Queued</c>.</summary>
    public bool HasQueuedCandidate { get; init; }

    /// <summary>The <c>MatchIngestStatus</c> name: "Idle" or "Processing".</summary>
    public string MatchIngestStatus { get; init; } = string.Empty;

    /// <summary>When the current lease was taken; null while Idle.</summary>
    public DateTime? MatchIngestClaimedAtUtc { get; init; }

    /// <summary>
    /// Age of the current claim. Compare against the Ingestor's
    /// <c>MatchIngestion:ClaimLeaseMinutes</c> (30 by default) to judge whether a
    /// run died holding the lease — the API does not make that call for you.
    /// </summary>
    public double? ClaimAgeSeconds { get; init; }

    public DateTime? LastMatchIngestAtUtc { get; init; }

    /// <summary>
    /// True when <see cref="LastMatchIngestAtUtc"/> is null: the account has been
    /// claimable but its lease has never come up. Never-ingested accounts sort
    /// first in the claim query, so this plus a tracked flag means the queue has
    /// not drained that far yet.
    /// </summary>
    public bool NeverIngested { get; init; }
}

/// <summary>
/// How many games the pipeline holds for this account. Three different numbers
/// exist and they are not interchangeable — each is reported with the population
/// it counts, because presenting any one of them as "games" is how the
/// retention-asymmetry trap (#927) gets sprung.
/// </summary>
public sealed record AccountExplorerMatchesIngestedReadModel
{
    /// <summary>
    /// Live <c>match_participants</c> rows for the PUUID: every champion, every
    /// queue, but <strong>bounded by retention</strong> (matches outside the
    /// retained patch window are hard-deleted).
    /// </summary>
    public long LiveParticipantCount { get; init; }

    /// <summary>
    /// Start of the surviving window, measured off the rows themselves rather
    /// than derived from the retention config. Null when nothing survives.
    /// </summary>
    public DateTime? OldestRetainedGameStartUtc { get; init; }

    /// <summary>End of the surviving window. Null when nothing survives.</summary>
    public DateTime? NewestRetainedGameStartUtc { get; init; }

    /// <summary>
    /// Career games summed from the frozen <c>champion_aggregate_scopes</c>,
    /// which retention never deletes — but the aggregation only ever folded
    /// <strong>main champions</strong>, so this is not the account's whole
    /// history either.
    /// </summary>
    public long CareerGamesFromAggregates { get; init; }

    /// <summary>Distinct patches covered by those frozen scopes.</summary>
    public int AggregatedPatchCount { get; init; }

    /// <summary>
    /// Oldest <c>LastGameStartTimeUtc</c> across the scopes — a lower bound on how
    /// far the frozen history reaches, since a scope records only its most recent
    /// game, not its first.
    /// </summary>
    public DateTime? OldestAggregatedGameStartUtc { get; init; }

    /// <summary>
    /// Sample size of the most recent <c>MainAnalysis</c> pass, capped by
    /// <c>MainAnalysis:MatchesToConsider</c> (50). A ceiling, not a total.
    /// </summary>
    public int? LastAnalysisSampleSize { get; init; }

    /// <summary>
    /// True when the frozen aggregates prove games existed that the live
    /// participant rows no longer hold. <strong>False does not mean nothing was
    /// pruned</strong> — the aggregates only cover main champions, so off-main
    /// games can be deleted without leaving any trace to detect.
    /// </summary>
    public bool Pruned { get; init; }

    /// <summary>The sentence explaining <see cref="Pruned"/> either way.</summary>
    public string PrunedNote { get; init; } = string.Empty;
}

/// <summary>
/// One <c>main_candidates</c> row: where it sits in the
/// New→Scored→Queued→Processing→Validated funnel, and what the scorer saw.
/// </summary>
public sealed record AccountExplorerCandidateReadModel
{
    public Guid Id { get; init; }

    public int ChampionId { get; init; }

    /// <summary>The <c>MainCandidateStatus</c> name.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// The <c>MainCandidateSource</c> name. Note that <c>ManualSeed</c> is never
    /// assigned in production — <c>ManualSeedProcess</c> reuses the ladder
    /// upsert, so a manually seeded candidate reads <c>Ladder</c>. Read
    /// <see cref="AccountExplorerReadModel.SeedRequest"/> for the manual trail.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>The 0–100 blend computed by <c>ScoringProcess</c>.</summary>
    public double Score { get; init; }

    /// <summary>
    /// The persisted inputs the score was computed from.
    /// <strong>The score's components are not stored</strong> — only the final
    /// blend is — so they cannot be shown. Recomputing them here would mix
    /// today's champion-scarcity snapshot into a number produced against an older
    /// one, and would silently disagree with <see cref="Score"/>.
    /// </summary>
    public AccountExplorerCandidateScoreInputsReadModel ScoreInputs { get; init; } = new();

    public DateTime DiscoveredAtUtc { get; init; }

    public DateTime? ScoredAtUtc { get; init; }

    public DateTime? ValidatedAtUtc { get; init; }
}

/// <summary>
/// What <c>ScoringProcess</c> had to work with. Ladder candidates carry mastery
/// rank/points; harvest candidates carry observed games/wins instead and leave
/// the mastery fields at zero.
/// </summary>
public sealed record AccountExplorerCandidateScoreInputsReadModel
{
    /// <summary>Mastery <c>lastPlayTime</c> (ladder) or last observed game (harvest) — the recency input.</summary>
    public DateTime LastPlayTimeUtc { get; init; }

    /// <summary>Rank of this champion in the account's mastery top-N; 0 for harvest candidates.</summary>
    public int ChampionRankInMasteryTop { get; init; }

    /// <summary>Mastery points; 0 for harvest candidates.</summary>
    public long ChampionPoints { get; init; }

    /// <summary>Games observed in orphan participant rows; 0 for ladder candidates.</summary>
    public int ObservedGames { get; init; }

    /// <summary>Wins among <see cref="ObservedGames"/>. Persisted but not a scoring input yet.</summary>
    public int ObservedWins { get; init; }
}

/// <summary>The main-champion rows and the thresholds that decided them.</summary>
public sealed record AccountExplorerMainsReadModel
{
    /// <summary>Rows for the account, highest play rate first.</summary>
    public IReadOnlyList<AccountExplorerMainRowReadModel> Rows { get; init; } = [];

    public AccountExplorerMainThresholdsReadModel Thresholds { get; init; } = new();
}

/// <summary>
/// The configured <c>MainAnalysis</c> thresholds, so a row's verdict can be read
/// against the rule that produced it.
/// </summary>
public sealed record AccountExplorerMainThresholdsReadModel
{
    /// <summary>
    /// Base play rate required to be a main for a well-covered champion (0.20).
    /// </summary>
    public double PlayRateThreshold { get; init; }

    /// <summary>
    /// Lowest the adaptive threshold can drop to, for a maximally under-covered
    /// champion (0.12, #407).
    /// </summary>
    public double PlayRateFloor { get; init; }

    public double OtpPlayRateThreshold { get; init; }

    /// <summary>
    /// Below this many analysed matches, <c>MainAnalysis</c> refuses to overwrite
    /// an account that already has an established main (#825).
    /// </summary>
    public int MinMatchesToEvaluate { get; init; }

    /// <summary>
    /// Why only a band is given: the effective per-champion threshold interpolates
    /// between the floor and the base threshold according to a live
    /// champion-coverage snapshot that is computed inside the Ingestor and never
    /// persisted. Naming an exact number here would be an invention.
    /// </summary>
    public string EffectiveThresholdNote { get; init; } = string.Empty;
}

/// <summary>One <c>main_champion_stats</c> row.</summary>
public sealed record AccountExplorerMainRowReadModel
{
    public int ChampionId { get; init; }

    /// <summary>Matches the analysis pass looked at (its sample size), not the account's total.</summary>
    public int TotalMatches { get; init; }

    public int ChampionMatches { get; init; }

    public double PlayRate { get; init; }

    public bool IsMain { get; init; }

    public bool IsOtp { get; init; }

    /// <summary>
    /// A main only thanks to the coverage-relaxed floor: its play rate sits below
    /// the base threshold (#407).
    /// </summary>
    public bool IsExtendedSample { get; init; }

    public bool IsActive { get; init; }

    public string PrimaryPosition { get; init; } = string.Empty;

    public IReadOnlyList<AccountExplorerPositionStatReadModel> PositionBreakdown { get; init; } = [];

    public DateTime CalculatedAtUtc { get; init; }

    /// <summary>
    /// True when the account's last <c>MainAnalysis</c> run is newer than this
    /// row's own <c>CalculatedAtUtc</c>: the process looked at the account and
    /// declined to overwrite — the thin-sample guard (#825). Not a stale-data bug.
    /// </summary>
    public bool AnalysisSkipped { get; init; }

    /// <summary>Null while the row is active.</summary>
    public AccountExplorerDeactivationReadModel? Deactivation { get; init; }
}

/// <summary>
/// What is knowable about a deactivated main row — which is less than one would
/// like, and this record says so rather than guessing.
/// </summary>
public sealed record AccountExplorerDeactivationReadModel
{
    /// <summary>
    /// The account's last successful mastery check. Deactivation is only
    /// trustworthy alongside this: a failed lookup leaves both the flag and the
    /// stamp untouched, so a null here means the retirement was never confirmed
    /// by a completed check.
    /// </summary>
    public DateTime? ConfirmedByActivityCheckAtUtc { get; init; }

    /// <summary>
    /// Always false: there is no retirement-reason column.
    /// <c>MainActivityProcess</c> writes the boolean and nothing else.
    /// </summary>
    public bool ReasonKnown { get; init; }

    /// <summary>The two causes the boolean collapses together, spelled out.</summary>
    public string ReasonNote { get; init; } = string.Empty;
}

/// <summary>One lane of a main row's position breakdown.</summary>
public sealed record AccountExplorerPositionStatReadModel
{
    public string Position { get; init; } = string.Empty;

    public int Games { get; init; }

    public double Rate { get; init; }
}

/// <summary>One <c>rank_snapshots</c> row.</summary>
public sealed record AccountExplorerRankSnapshotReadModel
{
    public DateTime CapturedAtUtc { get; init; }

    public string Tier { get; init; } = string.Empty;

    public string Division { get; init; } = string.Empty;

    public int LeaguePoints { get; init; }

    /// <summary>Queue totals from league-v4; null on snapshots taken before they were recorded.</summary>
    public int? Wins { get; init; }

    public int? Losses { get; init; }
}
