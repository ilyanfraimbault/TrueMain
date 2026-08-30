// Response shapes for the backend ops API, surfaced to the browser through the
// authenticated proxy at `/api/ops/*`. All fields are camelCase and every enum
// is a string. Longs are serialized as JS numbers by the backend.

/** Candidate pipeline buckets — `GET /api/ops/stats/overview` → `candidatesByStatus`. */
export interface CandidatesByStatus {
  New: number
  Scored: number
  Queued: number
  Processing: number
  Validated: number
  Rejected: number
}

/** `GET /api/ops/stats/overview`. */
export interface OverviewStats {
  trackedAccounts: number
  totalMatches: number
  totalParticipants: number
  candidatesByStatus: CandidatesByStatus
  totalMains: number
  totalOtps: number
  distinctChampionsWithGames: number
  distinctChampionsWithMains: number
  matchesLast7Days: number
  matchesLast30Days: number
}

/**
 * One row of `GET /api/ops/stats/champions` (sorted by `games` desc).
 *
 * NOTE: `mains`, `otps` and `extendedSamples` honor the `region` filter only —
 * they ignore `patch`/`position`/`queue`. `games` honors every filter.
 */
export interface ChampionStatsRow {
  championId: number
  games: number
  mains: number
  otps: number
  extendedSamples: number
}

/** Filters for `GET /api/ops/stats/champions`. Empty/undefined = no filter. */
export interface ChampionStatsFilters {
  /** PlatformId, e.g. `EUW1` / `KR` / `NA1`. */
  region?: string
  /** Normalized MAJOR.MINOR patch, e.g. `16.4`. */
  patch?: string
  /** `TOP` | `JUNGLE` | `MIDDLE` | `BOTTOM` | `UTILITY`. */
  position?: string
  /** Queue id, e.g. `420`. */
  queue?: number
}

/** X-axis granularity for `GET /api/ops/stats/matches-over-time`. */
export type MatchTimeGranularity = 'day' | 'week' | 'month' | 'year' | 'patch'

/**
 * One bucket of `GET /api/ops/stats/matches-over-time` (returned in chronological
 * order). Matches are counted by GAME date (`Match.GameStartTimeUtc`).
 *
 * `bucket` shape depends on the requested granularity:
 *   - day/week/month/year: ISO-8601 UTC timestamp of the period start
 *     (e.g. `2026-06-01T00:00:00Z`) — format the label client-side per granularity.
 *   - patch: the normalized `MAJOR.MINOR` version string (e.g. `16.4`) — use as-is.
 */
export interface MatchTimeBucket {
  bucket: string
  matches: number
}

/**
 * Granularities of the ingestion-throughput series. Narrower than
 * `MatchTimeGranularity` on purpose: a patch is a property of the games, not of
 * when we ingested them, and a year cannot fill two buckets under the 180-day
 * run retention.
 */
export type IngestionTimeGranularity = 'day' | 'week' | 'month'

/**
 * `GET /api/ops/stats/matches-ingested` — how many matches the pipeline actually
 * ingested per period (#1025), from the recorded MatchIngestion run summaries.
 *
 * A different question from `matches-over-time`, which buckets games by when they
 * were *played*: that one barely moves when ingestion stalls, and grows in the
 * past when a backfill lands. Sourced from run summaries rather than
 * `matches.CreatedAtUtc` because retention deletes matches, which would make an
 * old bucket shrink over time — a curve rewriting its own history.
 */
export interface MatchesIngested {
  /** Oldest first. Quiet periods inside the observed range are present at zero. */
  buckets: MatchesIngestedBucket[]
  /** The effective window in days, after the backend clamped the request. */
  windowDays: number
  /** The process_runs TTL in days — how far back run history can possibly go. */
  retentionDays: number
  /** Start of the oldest run seen, or null when the window holds none. */
  earliestRunAtUtc: string | null
}

export interface MatchesIngestedBucket {
  /** ISO-8601 UTC period start, same shape as `MatchTimeBucket.bucket`. */
  bucket: string
  matchesInserted: number
  /**
   * Seen and not written (already ingested, or filtered out). Carried because
   * inserted-alone cannot tell "nothing to do" from "working hard and storing
   * nothing", which are opposite operational states.
   */
  matchesSkipped: number
  timelinesUpdated: number
  /** Ingestion runs started in the period, summary or not. */
  runs: number
}

/** Which engine a storage object belongs to (#1023). Both share one volume. */
export type StorageEngine = 'postgres' | 'mongo'

/**
 * One row of `GET /api/ops/db/tables` (sorted by `totalBytes` desc across both
 * engines — the question is "what is biggest on this disk", which does not stop
 * at an engine boundary).
 */
export interface DbTableRow {
  engine: StorageEngine
  /** Postgres table name, or Mongo collection name. */
  tableName: string
  /** Planner estimate for Postgres, exact document count for Mongo. */
  rowEstimate: number
  totalBytes: number
  tableBytes: number
  indexBytes: number
}

/**
 * `GET /api/ops/db/history` — storage growth over a window plus the disk
 * forecast (#925). Read from the daily snapshot collection, never from a live
 * `pg_catalog` scan, so the page stays cheap however far back it looks.
 *
 * Everything is empty until the ingestor's storage-snapshot step has run at
 * least once, and `forecast` stays null until there are three days to fit.
 */
export interface DbStorageHistory {
  daily: DbStorageDailyPoint[]
  /** The largest objects only — smaller ones still count in `daily` totals. */
  tables: DbStorageTableSeries[]
  /**
   * The engines the window actually holds readings for. Says what the totals
   * cover instead of implying they are the whole disk: before the first Mongo
   * snapshot lands, and wherever Mongo is unconfigured, this is postgres alone.
   */
  engines: StorageEngine[]
  /**
   * How many of the most recent days the forecast is allowed to fit: the trailing
   * run of days measuring the same engines as the latest one. Equal to
   * `daily.length` in the steady state, smaller only just after an engine started
   * or stopped being measured. The panel reads this rather than re-deriving the
   * rule, so its explanation cannot drift from the backend's behaviour.
   */
  comparableDays: number
  /** Null when no honest projection is possible; see `DbStorageForecast`. */
  forecast: DbStorageForecast | null
}

export interface DbStorageDailyPoint {
  dateUtc: string
  /** Postgres + Mongo on-disk size — what actually occupies the volume. */
  databaseBytes: number
  /** The Postgres half of `databaseBytes`; 0 if the day has no Postgres reading. */
  postgresBytes: number
  /** The Mongo half; 0 for every day before #1023 and where Mongo is unconfigured. */
  mongoBytes: number
  /** Sum of per-object sizes; smaller than `databaseBytes` (no catalogs). */
  totalBytes: number
  rowEstimate: number
}

export interface DbStorageTableSeries {
  engine: StorageEngine
  tableName: string
  points: DbStorageTablePoint[]
  currentBytes: number
  bytesPerDay: number
  rowsPerDay: number
  /** Growth over the window as a fraction (0.25 = +25%); null if it started empty. */
  growthRate: number | null
}

export interface DbStorageTablePoint {
  dateUtc: string
  totalBytes: number
  rowEstimate: number
}

/**
 * Null on the parent when fewer than 3 days of history exist, when storage is
 * flat or shrinking, or when no disk capacity is configured — the panel explains
 * which rather than showing a made-up date.
 *
 * "Days of history" counts only the days measuring the same engines as the most
 * recent one: the day Mongo first appears adds its whole footprint at once, and
 * fitting a trend across that step would read a one-off jump as a daily rate.
 */
export interface DbStorageForecast {
  bytesPerDay: number
  diskCapacityBytes: number
  crossings: DbStorageThresholdCrossing[]
}

export interface DbStorageThresholdCrossing {
  percent: number
  thresholdBytes: number
  /** Null = no meaningful date at this rate (over a century either way). A past date = already breached. */
  projectedAtUtc: string | null
}

/**
 * `Abandoned` is a run that started but never recorded an outcome: its owning
 * ingestor died mid-flight. The backend assigns it either at startup (orphaned
 * `Running` rows are reconciled) or on read (a `Running` row whose heartbeat has
 * gone stale). It is a terminal state, distinct from `Failed`.
 *
 * `Skipped` is a run whose cadence guard decided this iteration was too early
 * (e.g. `Discovery:MinRunInterval`). Settled and healthy — but it did no work, so
 * it is deliberately not a `Success`: the backend excludes it both from the
 * cadence gate that reads "when did this last actually run?" and from the
 * consecutive-failure streak.
 */
export type ProcessRunStatus = 'Success' | 'Failed' | 'Running' | 'Abandoned' | 'Skipped'

/** One run row of `GET /api/ops/process-runs` → `runs`. */
export interface ProcessRun {
  id: number | string
  processName: string
  startedAtUtc: string
  finishedAtUtc: string | null
  durationMs: number
  status: ProcessRunStatus
  error: string | null
  host: string | null
  /**
   * Last liveness beat while the run was `Running` (null for legacy rows and
   * terminal runs that never beat). A `Running` run whose beat is older than the
   * backend's stale threshold is reported as `Abandoned`.
   */
  lastHeartbeatAtUtc: string | null
  summary: Record<string, unknown> | unknown[] | null
}

/** One rollup row of `GET /api/ops/process-runs` → `rollup`. */
export interface ProcessRollup {
  processName: string
  lastStatus: ProcessRunStatus
  lastRunAtUtc: string
  lastSuccessAtUtc: string | null
  /**
   * Failed runs inside the window. The window follows the request's `since`:
   * when `since` is omitted this is a true all-time total (≥ any narrower
   * window), not a hidden default.
   */
  failureCountInWindow: number
  /** All runs inside the same window — the denominator for `failureRateInWindow`. */
  runCountInWindow: number
  /**
   * Fraction of in-window runs that failed, in `[0, 1]` (0 when no runs fall
   * inside the window). Derived from real run counts — color failure health by
   * this rate rather than the always-positive absolute count.
   */
  failureRateInWindow: number
}

/** `GET /api/ops/process-runs` — one server-paginated page of runs + the rollup. */
export interface ProcessRunsResponse {
  runs: ProcessRun[]
  /** Per-process rollup over the FULL filtered set — unaffected by paging. */
  rollup: ProcessRollup[]
  /** Total runs matching the filters (across all pages). */
  total: number
  page: number
  pageSize: number
}

/**
 * The canonical ingestor pipeline chain, in execution order — one full pass runs
 * these processes in sequence (see `backend/Ingestor/Worker.cs`). Drives the
 * chain view: the ordered links and the per-iteration outcome lookup. Keep in
 * sync with the Worker's Full-mode sequence.
 */
export const PIPELINE_CHAIN: readonly string[] = [
  'Discovery',
  'ManualSeed',
  'Harvest',
  'Scoring',
  'MainActivity',
  'MatchIngestion',
  'MatchTeamPositionCorrection',
  'MainAnalysis',
  'MatchParticipantEloBracketEnrichment',
  'ChampionPatternAggregation',
  'ChampionMatchupLeadAggregation',
  'ChampionSynergyAggregation',
  'ChampionBanAggregation',
  'ChampionPowerspikeAggregation',
  'AccountRefresh',
  'MatchDataRetention',
  'StorageSnapshot',
]

/**
 * One pipeline iteration of `GET /api/ops/process-iterations` → `iterations`.
 * An iteration is one full pass of the chain; `runs` are its process runs in
 * pipeline order. `isRunning` is true while any run is still `Running` (this is
 * the pass the pipeline is currently in).
 */
export interface ProcessIteration {
  iterationId: string
  startedAtUtc: string
  lastActivityAtUtc: string
  isRunning: boolean
  runs: ProcessRun[]
}

/** `GET /api/ops/process-iterations` — one server-paginated page of iterations. */
export interface ProcessIterationsResponse {
  iterations: ProcessIteration[]
  /** Total iterations across all pages. */
  total: number
  page: number
  pageSize: number
}

/** Filters for `GET /api/ops/process-iterations`. */
export interface ProcessIterationsFilters {
  /** 1-based page index. */
  page?: number
  /** Iterations per page; backend clamps to [1, 50], default 10. */
  pageSize?: number
  /**
   * When true, the in-flight iteration is excluded from both the page and the
   * total, so a completed-history list paginates correctly. Default false.
   */
  finishedOnly?: boolean
}

/** Filters for `GET /api/ops/process-runs`. Empty/undefined = no filter. */
export interface ProcessRunsFilters {
  processName?: string
  status?: ProcessRunStatus
  /** ISO datetime lower bound. */
  since?: string
  /**
   * Legacy page size (pre-pagination): honored as `pageSize` when that param
   * is absent, superseded by it otherwise. Prefer `pageSize`.
   */
  limit?: number
  /** 1-based page index. */
  page?: number
  /** Rows per page; backend clamps to [1, 500], default 100. */
  pageSize?: number
}

/**
 * .NET `LogLevel` names, ascending in severity. Used by `GET /api/ops/logs`
 * where the `level` filter is a MINIMUM threshold (e.g. `Warning` returns
 * Warning + Error + Critical).
 */
export type LogLevel
  = | 'Trace'
    | 'Debug'
    | 'Information'
    | 'Warning'
    | 'Error'
    | 'Critical'

/** One row of `GET /api/ops/logs` → `entries` (newest first). */
export interface LogEntry {
  id: number | string
  timestampUtc: string
  level: LogLevel
  category: string
  message: string
  exception: string | null
  processName: string | null
  host: string | null
  /**
   * Registered ops-event name (e.g. `CandidateValidated`) when the row is a
   * named domain event; null for plain diagnostics.
   */
  eventType: string | null
}

/** `GET /api/ops/logs` — server-paginated log entries. */
export interface LogsResponse {
  entries: LogEntry[]
  /** Total rows matching the filters (across all pages). */
  total: number
  page: number
  pageSize: number
  /**
   * Every known ops-event name (static backend catalog, independent of the
   * active filters) — feeds the event filter select.
   */
  eventTypes: string[]
  /** The producing processes ("Api", "Ingestor") — feeds the process filter select. */
  processes: string[]
}

/** Filters for `GET /api/ops/logs`. Empty/undefined = no filter. */
export interface LogsFilters {
  /** Minimum severity threshold (a `LogLevel` name). */
  level?: LogLevel
  /** Exact category (namespace) match. */
  category?: string
  /** ISO datetime lower bound. */
  since?: string
  /** Case-insensitive substring match on message/exception. */
  search?: string
  /** Exact (case-insensitive) ops-event name; omit for all rows. */
  eventType?: string
  /** Exact (case-insensitive) process name ("Api"/"Ingestor"); omit for all. */
  process?: string
  /** True keeps only rows carrying a formatted exception; omit/false = no filter. */
  hasException?: boolean
  /** 1-based page index. */
  page?: number
  /** Rows per page; backend clamps to [1, 200], default 50. */
  pageSize?: number
}

/**
 * What triggered a crash report. `UncleanShutdown` is detected at the next boot
 * (the previous run vanished with no graceful stop — OOM/SIGKILL/hard crash) and
 * carries no stack trace, just the dead run's last-known memory snapshot.
 */
export type CrashSource
  = | 'AppDomainUnhandled'
    | 'TaskSchedulerUnobserved'
    | 'HostRun'
    | 'UncleanShutdown'

/** One link in a crash's exception chain. */
export interface CrashException {
  type: string
  message: string
  stackTrace: string | null
}

/** One buffered log line (Information+) captured just before the crash. */
export interface CrashLogTailEntry {
  timestampUtc: string
  level: LogLevel
  category: string
  message: string
  exception: string | null
}

/** One row of `GET /api/ops/crashes` → `entries` (newest first). */
export interface CrashReport {
  /** The crash document id (Mongo ObjectId hex string). */
  id: string
  timestampUtc: string
  /** Producing process: "Api" or "Ingestor". */
  processName: string
  source: CrashSource
  /**
   * Plain-language reading of the crash derived server-side from the source,
   * exception chain and memory snapshot (#722). Heuristic display text — the
   * raw fields stay authoritative.
   */
  explanation: string
  /** Top-level exception type; null for an unclean shutdown. */
  exceptionType: string | null
  message: string | null
  stackTrace: string | null
  innerExceptions: CrashException[]
  host: string | null
  osDescription: string | null
  /** Process lifetime at the crash, in seconds. */
  uptimeSeconds: number
  runtimeVersion: string | null
  appVersion: string | null
  workingSetBytes: number
  totalManagedMemoryBytes: number
  gen0Collections: number
  gen1Collections: number
  gen2Collections: number
  exitCode: number | null
  /** Last log lines before the crash, oldest-first. */
  recentLogTail: CrashLogTailEntry[]
}

/** `GET /api/ops/crashes` — server-paginated crash reports. */
export interface CrashesResponse {
  entries: CrashReport[]
  /** Total rows matching the filters (across all pages). */
  total: number
  page: number
  pageSize: number
  /** Every `CrashSource` name (static catalog) — feeds the source filter select. */
  sources: string[]
  /** The producing processes ("Api", "Ingestor") — feeds the process filter select. */
  processes: string[]
}

/** Filters for `GET /api/ops/crashes`. Empty/undefined = no filter. */
export interface CrashesFilters {
  /** ISO datetime lower bound. */
  since?: string
  /** Exact process name ("Api"/"Ingestor"); omit for all. */
  process?: string
  /** Exact (case-insensitive) `CrashSource` name; omit for all. */
  source?: CrashSource
  /** Case-insensitive substring match on message/stack trace. */
  search?: string
  /** 1-based page index. */
  page?: number
  /** Rows per page; backend clamps to [1, 100], default 25. */
  pageSize?: number
}

/**
 * Lifecycle of a seed request (`POST /api/ops/accounts/seed`):
 *   Pending   — accepted, not yet picked up
 *   Resolving — resolving the Riot ID → PUUID / account
 *   Ingested  — account + mastery-derived candidates created and queued. NOTE:
 *               actual match ingestion + main classification happen on the next
 *               Ingestor cycle, NOT synchronously here.
 *   Failed    — resolution/queueing failed; see `error`.
 */
export type SeedRequestStatus = 'Pending' | 'Resolving' | 'Ingested' | 'Failed'

/** A status that will not change on its own — polling can stop. */
export const TERMINAL_SEED_STATUSES: readonly SeedRequestStatus[] = ['Ingested', 'Failed']

/**
 * `GET /api/ops/accounts/seed/{id}` and one row of
 * `GET /api/ops/accounts/seed`. Resolved identifiers are `null` until the
 * request reaches `Ingested`.
 */
export interface SeedRequestReadModel {
  id: string
  gameName: string
  tagLine: string
  platformId: string
  status: SeedRequestStatus
  error: string | null
  requestedAtUtc: string
  processedAtUtc: string | null
  resolvedPuuid: string | null
  resolvedRiotAccountId: string | null
}

/** Body for `POST /api/ops/accounts/seed`. */
export interface SeedAccountBody {
  gameName: string
  tagLine: string
  /** PlatformId, e.g. `EUW1` / `KR` / `NA1`. */
  platformId: string
}

/** `202` response of `POST /api/ops/accounts/seed`. */
export interface SeedAccountResponse {
  id: string
  status: SeedRequestStatus
  /**
   * `true` when this call created a new seed request; `false` when an existing
   * (still-unprocessed) request for the same Riot ID + platform was returned
   * idempotently — i.e. the account was already queued/seeded.
   */
  created: boolean
}

/**
 * `GET /api/ops/accounts/seed` — one page of seed requests, newest-first, with the
 * total matching the same filters so the panel can render a pager (#1166).
 *
 * Shaped like `CandidatesResponse` deliberately: both lists sit on `/candidates`
 * and page identically.
 */
export interface SeedRequestsResponse {
  requests: SeedRequestReadModel[]
  /** Total rows matching the filters (across all pages). */
  total: number
  page: number
  pageSize: number
}

/** Filters for `GET /api/ops/accounts/seed`. Empty/undefined = no filter. */
export interface SeedRequestsFilters {
  /** A `SeedRequestStatus` name. */
  status?: SeedRequestStatus
  /** Case-insensitive substring match on the Riot ID (gameName/tagLine). */
  search?: string
  /** PlatformId, e.g. `EUW1` / `KR` / `NA1`. A value the backend cannot parse is a 400. */
  region?: string
  /** 1-based page index. */
  page?: number
  /** Rows per page; backend clamps to [1, 100], default 25. */
  pageSize?: number
}

// =============================================================================
// Candidates — `GET /api/ops/candidates` (the ingestion pipeline list)
// =============================================================================

/**
 * Lifecycle of a main candidate (the ingestion pipeline):
 *   New        — surfaced from mastery, not yet scored
 *   Scored     — a main-likelihood score has been computed
 *   Queued     — selected for full ingestion
 *   Processing — the Ingestor is pulling the account's matches
 *   Validated  — confirmed as a main and fully ingested
 *   Rejected   — ruled out (not a main)
 */
export type MainCandidateStatus
  = | 'New'
    | 'Scored'
    | 'Queued'
    | 'Processing'
    | 'Validated'
    | 'Rejected'

/**
 * One row of `GET /api/ops/candidates`. `gameName`/`tagLine` are joined from the
 * `RiotAccount` on PUUID and are `null` until the account has been resolved (a
 * candidate is discovered from mastery before its account is upserted).
 */
export interface CandidateRow {
  id: string
  platformId: string
  puuid: string
  gameName: string | null
  tagLine: string | null
  championId: number
  championPoints: number
  championRankInMasteryTop: number
  score: number
  status: MainCandidateStatus
  discoveredAtUtc: string
  scoredAtUtc: string | null
  validatedAtUtc: string | null
  lastPlayTimeUtc: string
}

/** `GET /api/ops/candidates` — server-paginated candidate rows, most-relevant first. */
export interface CandidatesResponse {
  candidates: CandidateRow[]
  /** Total rows matching the filters (across all pages). */
  total: number
  page: number
  pageSize: number
}

/** Filters for `GET /api/ops/candidates`. Empty/undefined = no filter. */
export interface CandidatesFilters {
  /** A `MainCandidateStatus` name. */
  status?: MainCandidateStatus
  /** PlatformId, e.g. `EUW1` / `KR` / `NA1`. */
  region?: string
  /** Riot ID (gameName/tagLine), PUUID, or champion-id search. */
  search?: string
  /** 1-based page index. */
  page?: number
  /** Rows per page; backend clamps to [1, 100], default 25. */
  pageSize?: number
}

/**
 * `GET /api/ops/candidates/{id}` — one candidate's full detail: its pipeline
 * fields plus the ingested match count for its PUUID and the linked manual
 * `seedRequest` (matched on `resolvedPuuid` + platform), `null` when the
 * candidate was discovered organically by the ladder.
 */
export interface CandidateDetail extends CandidateRow {
  ingestedMatchCount: number
  seedRequest: SeedRequestReadModel | null
}

/**
 * `GET /api/ops/candidates/funnel` (#1024) — candidate throughput per period, read
 * from the recorded process-run summaries rather than from `main_candidates` row
 * counts: retention prunes stale candidates, so counting rows by status per past
 * period under-reports every bucket and increasingly so the further back it looks.
 * The whole series is therefore bounded by the `process_runs` TTL.
 */
export interface CandidateFunnel {
  buckets: CandidateFunnelBucket[]
  /** The requested window in days, after backend clamping. */
  windowDays: number
  /** How long run history is kept — the hard bound on `windowDays`. */
  retentionDays: number
  /**
   * Start of the oldest run in the window: the earliest period the series can speak
   * for. `null` when no run survives at all — an empty range, not a range of zeros.
   */
  earliestRunAtUtc: string | null
  /**
   * Start of the first run that recorded the validated counter, which the ingestor
   * only began writing with #1024. Buckets before it carry `validated: null`.
   */
  validatedFirstMeasuredAtUtc: string | null
}

/**
 * One period of the funnel. Intake is split by producing process because the three
 * sources fail independently — the ladder drying up and the harvest drying up are
 * different incidents with the same total.
 */
export interface CandidateFunnelBucket {
  /** Period start, ISO-8601 UTC. */
  bucket: string
  /** Candidates inserted by ladder discovery. */
  intakeLadder: number
  /** Candidates inserted by the orphan-participant harvest. */
  intakeHarvest: number
  /** Candidates an operator's manual seed pushed into the queue. */
  intakeManual: number
  scored: number
  /** Candidates promoted to the ingestion queue — the per-platform top-N. */
  promoted: number
  /** Accounts that cleared ingestion. `null`, not `0`, before the counter existed. */
  validated: number | null
  /** Accounts demoted back out of Validated on a critical play rate. */
  demoted: number
  /** Runs of any contributing process in this period; `0` means the pipeline was idle. */
  runs: number
}

/**
 * `GET /api/ops/candidates/queue-latency` (#1024) — how long the candidates that
 * exist *right now* took to move through the queue. A snapshot over retained rows,
 * never a historical average: pruned candidates are not in it, and the surviving
 * population skews towards the ones that did move. Label it as such wherever shown.
 */
export interface CandidateQueueLatency {
  /** Discovery → scoring, over candidates that have been scored. */
  discoveredToScored: CandidateLatencyLeg
  /** Scoring → cleared ingestion, over candidates that have been validated. */
  scoredToValidated: CandidateLatencyLeg
  /** Candidate rows currently retained — the population every leg is drawn from. */
  retainedCandidates: number
  asOfUtc: string
}

/**
 * One leg of the queue. Both percentiles are `null` when `samples` is 0 — no row
 * carried both ends of the leg, which is not a latency of zero.
 */
export interface CandidateLatencyLeg {
  samples: number
  medianSeconds: number | null
  /** The slow tail: it diverging from the median is the shape of a backed-up queue. */
  p90Seconds: number | null
}

// =============================================================================
// Data quality — `GET /api/ops/data-quality/*`
// =============================================================================

/**
 * The data-quality checks, camelCase on the wire. Each check is independently
 * listable and queue-scoped (lane checks don't fire on ARAM):
 *   - `missingTimeline`      — TimelineIngested=false past the staleness window
 *   - `wrongParticipantCount`— row count ≠ the queue's expected count
 *   - `missingTeamPosition`  — a team missing one of the 5 lanes (SR only)
 *   - `zeroDuration`         — GameDurationSeconds = 0
 *   - `duplicateChampion`    — same champion twice on one team (SR only)
 */
export type DataQualityIssueType
  = | 'missingTimeline'
    | 'wrongParticipantCount'
    | 'missingTeamPosition'
    | 'zeroDuration'
    | 'duplicateChampion'

/** Nuxt UI badge/icon color used by the admin panels' status/severity badges. */
export type BadgeColor = 'error' | 'warning' | 'info' | 'neutral' | 'success' | 'primary'

/**
 * Presentation metadata for one issue type — label, icon and badge color. Drives
 * the filter select, group headers and badges so the panel stays consistent.
 * Keyed by `DataQualityIssueType` in `ISSUE_META` on the data-quality page.
 */
export interface IssueMeta {
  label: string
  icon: string
  color: BadgeColor
  description: string
}

/** A single flagged match row in the list. */
export interface FlaggedMatch {
  matchId: string
  platformId: string
  queueId: number
  gameStartTimeUtc: string
  gameDurationSeconds: number
  timelineIngested: boolean
  participantCount: number
  /** Expected count for the queue, or null when the queue has no profile. */
  expectedParticipantCount: number | null
  /** Every check this match trips (a match can appear in several groups). */
  issues: DataQualityIssueType[]
}

/** One issue type's flagged matches: a capped sample plus the full count. */
export interface DataQualityIssueGroup {
  issueType: DataQualityIssueType
  count: number
  matches: FlaggedMatch[]
}

/** `GET /api/ops/data-quality/incomplete-matches` — flagged matches by issue. */
export interface IncompleteMatchesResponse {
  groups: DataQualityIssueGroup[]
  /** Distinct matches flagged by at least one active check. */
  total: number
  page: number
  pageSize: number
  /** Hours a missing timeline must age before it's flagged (vs normally pending). */
  staleTimelineThresholdHours: number
}

/** Filters for `GET /api/ops/data-quality/incomplete-matches`. */
export interface IncompleteMatchesFilters {
  /** Restrict to a single check; omit for all. */
  issue?: DataQualityIssueType
  /** Restrict to one queue id (e.g. 420); omit for all. */
  queue?: number
  /** Only consider matches at least this many hours old. */
  minAgeHours?: number
  /** 1-based page index for each issue group's sample. */
  page?: number
  /** Per-issue sample size; backend clamps to [1, 100], default 25. */
  pageSize?: number
}

/**
 * One position slot on a team. For lane queues `position` is one of the five
 * canonical lanes and `filled` is false for a gap; for laneless queues
 * `position` is empty and every slot is filled.
 */
export interface MatchSlot {
  /** Canonical lane name for lane queues; empty for laneless queues. */
  position: string
  /** False when this lane slot has no participant (a gap to highlight). */
  filled: boolean
  participantId: number | null
  championId: number | null
  summonerName: string | null
  win: boolean | null
  /** True when this slot shares its champion with another slot on the team. */
  duplicateChampion: boolean
}

/** One team's roster, laid out by position with gaps highlighted. */
export interface MatchTeam {
  teamId: number
  /** Actual participant rows ingested for this team. */
  playerCount: number
  /** Players a complete team should carry, or null when the queue is unknown. */
  expectedPlayerCount: number | null
  /**
   * Members whose position didn't map onto a canonical lane (unknown/duplicate
   * position). They exist — the team isn't short — so they're reported as
   * unplaced, never as missing players. Always 0 for laneless queues.
   */
  unplacedCount: number
  /** Team result, or null when the team has no ingested rows. */
  win: boolean | null
  slots: MatchSlot[]
}

/** `GET /api/ops/data-quality/match/{id}` — per-match detail. */
export interface MatchDataQualityDetail {
  matchId: string
  platformId: string
  queueId: number
  gameMode: string
  gameStartTimeUtc: string
  gameDurationSeconds: number
  gameVersion: string
  timelineIngested: boolean
  participantCount: number
  expectedParticipantCount: number | null
  /** True when the queue has a known profile (count/position rules apply). */
  queueKnown: boolean
  /** True when TeamPosition is meaningful for this queue. */
  hasLanes: boolean
  issues: DataQualityIssueType[]
  teams: MatchTeam[]
}

/** Relative window for `GET /api/ops/riot-usage`. Also fixes the chart bucket size. */
export type RiotUsageWindow = '1h' | '24h' | '7d'

/** Filters for `GET /api/ops/riot-usage`. */
export interface RiotUsageFilters {
  window?: RiotUsageWindow
  /** Restrict to one endpoint key (e.g. `match-v5.match`). Omit for all. */
  endpoint?: string
}

/** Per-endpoint rollup row of `GET /api/ops/riot-usage` (sorted by `calls` desc). */
export interface RiotEndpointUsage {
  /** Stable Riot "method" key, e.g. `match-v5.match`, `league-v4.challenger`. */
  endpoint: string
  calls: number
  successes: number
  errors: number
  avgLatencyMs: number
  lastCalledAtUtc: string
  /** Freshest `X-Method-Rate-Limit` header seen for this endpoint, or null (#1035). */
  methodRateLimit: string | null
  /** Freshest `X-Method-Rate-Limit-Count` header seen for this endpoint, or null (#1035). */
  methodRateLimitCount: string | null
}

/** One status-code histogram row. `statusCode` 0 means a transport fault (no response). */
export interface RiotStatusCount {
  statusCode: number
  count: number
}

/** One time bucket of the call-volume series (chronological, ISO-8601 UTC bucket start). */
export interface RiotUsageBucket {
  bucketUtc: string
  calls: number
  errors: number
  /** Subset of `calls` that landed a 429 — budget spent for no data (#1035). */
  retries: number
}

/** Calls attributed to one caller process (#1035). `"unknown"` when unattributed. */
export interface RiotCallerUsage {
  caller: string
  calls: number
  errors: number
}

/** The app rate-limit window with the smallest sustained-load daily ceiling (#1035). */
export interface RiotBindingLimit {
  limit: number
  windowSeconds: number
  maxCallsPerDay: number
}

/**
 * Budget-headroom estimate (#1035): "how many more tracked accounts fit", always
 * computed over the last 7 days regardless of the panel's selected window.
 * `sufficientData` is `false` — with only `observedWindowHours`/`requiredWindowHours`
 * set — when there isn't enough rollup history yet, no accounts are tracked, or no
 * rate-limit snapshot was seen.
 */
export interface RiotApiHeadroom {
  sufficientData: boolean
  observedWindowHours: number
  requiredWindowHours: number
  trackedAccounts: number
  callsPerAccountPerDay: number | null
  observedCallsPerDay: number | null
  bindingLimit: RiotBindingLimit | null
  spareCallsPerDay: number | null
  additionalAccountsHeadroom: number | null
}

/**
 * Latest rate-limit header snapshot in the window (or null when no call carried
 * rate-limit headers). `appRateLimit`/`appRateLimitCount` are Riot's raw
 * `X-App-Rate-Limit[-Count]` strings (e.g. limit `20:1,100:120`, count
 * `3:1,57:120`) — windows are `count:seconds` pairs.
 */
export interface RiotRateLimit {
  observedAtUtc: string
  appRateLimit: string | null
  appRateLimitCount: string | null
  methodRateLimit: string | null
  methodRateLimitCount: string | null
  retryAfterSeconds: number | null
  rateLimitType: string | null
}

/** `GET /api/ops/riot-usage` — Riot API usage metrics over a window (#93). */
export interface RiotApiUsage {
  window: RiotUsageWindow
  sinceUtc: string
  generatedAtUtc: string
  totalCalls: number
  totalErrors: number
  /** Errors / total calls in [0, 1]; 0 when there were no calls. */
  errorRate: number
  avgLatencyMs: number
  endpoints: RiotEndpointUsage[]
  statusCodes: RiotStatusCount[]
  timeSeries: RiotUsageBucket[]
  rateLimit: RiotRateLimit | null
  /** Calls attributed to each caller process, ordered by `calls` descending (#1035). */
  callerBreakdown: RiotCallerUsage[]
  headroom: RiotApiHeadroom
}

/**
 * One aggregation family from `GET /api/ops/stats/aggregations` — a group of
 * aggregate tables produced by a single ingestor process (builds patterns,
 * matchups, synergies, powerspikes, mains).
 */
export interface AggregationFamily {
  /** Stable identifier: "builds" | "matchups" | "synergies" | "powerspikes" | "mains". */
  key: string
  /** The recorded ingestor process producing this family. */
  processName: string
  tables: { table: string, rows: number }[]
  totalRows: number
  distinctChampions: number
  /** Distinct normalized patches; null when the family has no patch axis (mains). */
  distinctPatches: number | null
  /** Most recent aggregate-row write — data freshness independent of run records. */
  lastAggregatedAtUtc: string | null
  /** Latest recorded run of the producing process; null when it never ran. */
  lastRun: AggregationRun | null
}

/**
 * Rollup of the producing process's runs: the latest run's outcome plus the
 * last success (they differ exactly when the latest run failed/was abandoned).
 */
export interface AggregationRun {
  status: string
  lastStartedAtUtc: string | null
  lastFinishedAtUtc: string | null
  lastSuccessAtUtc: string | null
  durationMs: number | null
  /** JSONB summary the process returned on its last success (per-run counts). */
  lastSuccessSummary: Record<string, unknown> | null
}

/** Aggregation-side backlogs — all read zero when the pipeline is caught up. */
export interface AggregationBacklog {
  /** Queue-scoped timeline-ingested matches not yet folded into powerspikes. */
  pendingPowerspikeMatches: number
  /**
   * Queue-scoped matches not yet folded into the synergy aggregates. Starts at the
   * full retained match count on the first deploy (the fold flag ships false for
   * every existing row on purpose) and drains over the following runs.
   */
  pendingSynergyMatches: number
  /** Tracked participants still missing their elo bracket stamp. */
  pendingEloBracketParticipants: number
  /** Queue-scoped matches with an ingested timeline (backlog denominator). */
  timelineIngestedMatches: number
}

/** `GET /api/ops/stats/aggregations` — the Aggregation panel payload. */
export interface AggregationsResponse {
  queueId: number
  families: AggregationFamily[]
  backlog: AggregationBacklog
}

/**
 * A detector verdict. `unknown` means the measurement could not be taken — it is
 * never used for "measured and fine", so a card is only green when something was
 * actually checked.
 */
export type DetectorStatus = 'green' | 'amber' | 'red' | 'unknown'

/** One drill-down row: an audited table, platform, process, check or patch. */
export interface DataQualityDetectorRow {
  label: string
  status: DetectorStatus
  /** The row's number, or null when it could not be measured. */
  value: number | null
  /** The number as it should be printed, with its unit; null when unmeasured. */
  valueLabel: string | null
  note: string | null
}

/** One configured green/amber/red boundary, echoed so the panel can state it. */
export interface DataQualityThreshold {
  label: string
  /** Null when the level is disabled (configured to 0 or less). */
  amber: number | null
  red: number | null
  unit: 'count' | 'percent' | 'hours' | 'ratio'
  /**
   * Which side of the level is the bad one. `below` marks a floor (patch volume
   * against the median); everything else is a ceiling. The number alone does not
   * say which, and printing a floor as a ceiling inverts its meaning.
   */
  direction: 'above' | 'below'
}

/** One detector's card (#924). */
export interface DataQualityDetector {
  key: string
  title: string
  status: DetectorStatus
  /** Headline number, or null when unknown. */
  count: number | null
  countLabel: string
  headline: string
  /** Set if and only if `status` is `unknown`. */
  unknownReason: string | null
  /** Which tables the detector reads and why that is affordable on a page view. */
  sourceNote: string
  rows: DataQualityDetectorRow[]
  thresholds: DataQualityThreshold[]
  /** True when a heavier on-demand endpoint can expand this detector. */
  hasDrillDownEndpoint: boolean
}

/** `GET /api/ops/data-quality/detectors` — the automated anomaly detectors. */
export interface DataQualityDetectorsResponse {
  detectors: DataQualityDetector[]
  /** Every age on the panel is relative to this, not to the browser's clock. */
  evaluatedAtUtc: string
}

/** One champion's aggregate freshness on a patch. */
export interface ChampionFreshnessRow {
  championId: number
  patch: string
  lastAggregatedAtUtc: string
  ageHours: number
  /** Scope rows behind the reading, so a one-account champion reads as thin. */
  scopeRows: number
  status: DetectorStatus
}

/** `GET /api/ops/data-quality/aggregate-freshness` — on-demand breakdown. */
export interface AggregateFreshnessResponse {
  patches: string[]
  champions: ChampionFreshnessRow[]
  championCount: number
  staleChampionCount: number
  staleAfterHours: number
  evaluatedAtUtc: string
}

/**
 * One cockpit signal (#1031): a verdict, the sentence behind it, and the route that
 * owns its detail. The cockpit holds no depth of its own — every tile is a link.
 */
export interface PipelineHealthSignal {
  /** `processes` | `dataQuality` | `ingestionLag` | `diskForecast`. */
  key: string
  title: string
  status: DetectorStatus
  /** The one sentence explaining this signal's state. */
  headline: string
  /**
   * Set if and only if `status` is `unknown`. Rendered in place of a number, because
   * a zero here would read as a pass.
   */
  unknownReason: string | null
  /** Admin route owning the detail: `/processes`, `/data-quality`, `/database`. */
  detailPath: string
}

/** Effective status of one pipeline process, PascalCase as `/ops/process-runs` spells it. */
export type ProcessHealthStatus = 'Success' | 'Failed' | 'Running' | 'Abandoned' | 'Skipped' | 'Missing'

/** One process's run health. Timestamps are null when it has never recorded a run. */
export interface ProcessHealth {
  processName: string
  status: ProcessHealthStatus
  lastStartedAtUtc: string | null
  lastFinishedAtUtc: string | null
  /** Null when the process has never succeeded — not the same as "succeeded long ago". */
  lastSuccessAtUtc: string | null
  /** Terminal runs since the last success; 0 when the latest run succeeded or was skipped. */
  consecutiveFailures: number
  durationMs: number
  error: string | null
}

/** Newest ingested match and patch on one platform. */
export interface PlatformRawDataFreshness {
  platformId: string
  latestMatchStartAtUtc: string | null
  latestPatchVersion: string
}

/** Raw-corpus counters, scoped to the configured ranked queue. */
export interface RawDataFreshness {
  queueId: number
  rawMatchCount: number
  rawParticipantCount: number
  platforms: PlatformRawDataFreshness[]
}

/** The two pipeline gaps. Null means "nothing to measure", never zero. */
export interface PipelineGaps {
  matchIngestionToMainAnalysisMinutes: number | null
  championDataLagMinutes: number | null
}

/**
 * `GET /api/ops/pipeline-health` — the health cockpit's single payload (#1031).
 * One rolled-up verdict over the signals below, each of which links to the panel that
 * owns its detail.
 */
export interface PipelineHealth {
  status: DetectorStatus
  /** The verdict as one actionable sentence. */
  headline: string
  /** Stated on the page: a cockpit that hides its age gets read as live. */
  evaluatedAtUtc: string
  /** Severity-ordered, worst first. */
  signals: PipelineHealthSignal[]
  processes: ProcessHealth[]
  rawData: RawDataFreshness
  gaps: PipelineGaps
}

// =============================================================================
// Patch coverage — `GET /api/ops/patch-coverage` (#1033)
// =============================================================================

/**
 * Why a patch is or is not servable. `notAggregated` and `thin` are deliberately
 * distinct: both mean "almost nothing clears the floor", and they call for
 * opposite reactions — wait for the fold, versus stop trusting the patch.
 */
export type PatchVerdict = 'servable' | 'thin' | 'notAggregated' | 'unknown'

/** One game date's ingestion on a patch. */
export interface PatchCoverageDay {
  /** UTC game date, ISO `yyyy-MM-dd`. */
  date: string
  matches: number
  participants: number
}

/** A `(champion, lane)` line that has games but not enough of them. */
export interface PatchThinLine {
  championId: number
  position: string
  games: number
  /** Games still missing before the line clears the floor. */
  gamesToFloor: number
}

/** One aggregation fold's state on one patch. */
export interface PatchFoldCoverage {
  key: string
  label: string
  /**
   * False when the patch predates the fold entirely (#920 bans, #957 per-opponent
   * spikes). Every count is then `null` rather than `0`: raw matches are not kept,
   * so those rows are absent by construction, not missing — and a zero would read
   * as "the fold is broken on this patch".
   */
  measured: boolean
  /** Oldest patch the fold has any row on, or null when it has none at all. */
  firstMeasuredPatch: string | null
  /** Set if and only if `measured` is false. */
  notMeasuredNote: string | null
  rows: number | null
  champions: number | null
  lastAggregatedAtUtc: string | null
  ageHours: number | null
  status: DetectorStatus
  /** Matches still to fold, or null for folds carrying no per-match flag. */
  pendingMatches: number | null
  note: string | null
}

/** One patch's ingestion, aggregate coverage and per-fold state. */
export interface PatchCoverageRow {
  patch: string
  /** True for the patch the public reads currently resolve to. */
  isCurrent: boolean
  verdict: PatchVerdict
  status: DetectorStatus
  headline: string
  matches: number
  participants: number
  firstGameStartUtc: string | null
  lastGameStartUtc: string | null
  daily: PatchCoverageDay[]
  /** `(champion, lane)` pairs holding at least one aggregate row. */
  lines: number
  linesPastFloor: number
  champions: number
  championsPastFloor: number
  /** The bar `linesPastFloor` was judged against; null when the patch was not judged. */
  servableLinesBar: number | null
  servableLinesBarNote: string | null
  belowFloorCount: number
  belowFloor: PatchThinLine[]
  folds: PatchFoldCoverage[]
}

/** `GET /api/ops/patch-coverage` — is the current patch servable? (#1033) */
export interface PatchCoverageResponse {
  queueId: number
  /** Echoed from `ChampionsList:MinSampleGames`, never re-declared admin-side. */
  minSampleGames: number
  floorNote: string
  /** Newest patch holding an aggregate row — what the public reads resolve to. */
  currentPatch: string | null
  verdict: PatchVerdict
  status: DetectorStatus
  headline: string
  /**
   * Why no verdict could be given. Set only when a measurement failed — without the
   * coverage rollup, `thin` and `notAggregated` are indistinguishable, and guessing
   * between them is worse than saying nothing.
   */
  unknownReason: string | null
  patches: PatchCoverageRow[]
  sourceNote: string
  evaluatedAtUtc: string
}

// =============================================================================
// Account explorer — `GET /api/ops/accounts/{nameTag}` (#1032)
// =============================================================================

/**
 * The one-word verdict on a Riot ID, resolved server-side first-match-wins:
 *   NeverDiscovered   — no account row and no seed request. Says nothing about
 *                       whether the Riot ID exists: this read never calls Riot.
 *   SeedRequestedOnly — no account row, but an operator asked for it; the seed
 *                       request's own status/error says why it has not landed.
 *   Invalidated       — the PUUID 404s and AccountRefresh could not recover it.
 *                       Excluded from every selection: nothing will move again.
 *   Tracked           — in the match-ingestion population (queued candidate,
 *                       active main, or both).
 *   Retired           — had mains, MainActivity deactivated all of them (#900).
 *                       Rows are flagged, never deleted.
 *   NotAMain          — analysed, but nothing cleared the adaptive IsMain floor.
 *   CandidateOnly     — in the candidate funnel, never analysed.
 *   Discovered        — the account exists and nothing else has happened to it.
 */
export type AccountPipelineState
  = | 'NeverDiscovered'
    | 'SeedRequestedOnly'
    | 'Invalidated'
    | 'Tracked'
    | 'Retired'
    | 'NotAMain'
    | 'CandidateOnly'
    | 'Discovered'

/**
 * `GET /api/ops/accounts/{nameTag}?region=` — everything the pipeline knows about
 * one Riot ID. Never 404s: an unknown Riot ID is a populated response in the
 * `NeverDiscovered` state, because that is an answer this page exists to give.
 * 400 only on a malformed Riot ID or an unknown region.
 *
 * `identity`, `tracking` and `matchesIngested` are `null` together — they all
 * require a resolved account row.
 */
export interface AccountExplorer {
  query: AccountExplorerQuery
  state: AccountPipelineState
  /** The state in a sentence, built server-side. Render it verbatim. */
  stateDetail: string
  identity: AccountExplorerIdentity | null
  /**
   * Other accounts carrying the same Riot ID. `(gameName, tagLine, platformId)`
   * is deliberately not unique — Riot IDs are recyclable and collide across
   * regions — so the resolver picks the most recently active and lists the rest
   * here instead of arbitrating in silence. Usually empty.
   */
  otherAccountsWithSameRiotId: AccountExplorerAccountRef[]
  tracking: AccountExplorerTracking | null
  matchesIngested: AccountExplorerMatchesIngested | null
  /**
   * `main_candidates` rows, highest score first. Always empty when `identity` is
   * null: candidates are keyed on (platformId, puuid) and carry no Riot ID, so a
   * candidate whose account is not upserted yet cannot be found from a Riot ID.
   */
  candidates: AccountExplorerCandidate[]
  /** The manual "add a main" trail — the only reliable manual-seed signal. */
  seedRequest: SeedRequestReadModel | null
  mains: AccountExplorerMains
  /**
   * Most recent first, capped at 50. At most one row per UTC day, solo queue
   * only, never pruned — the one series here whose gaps are gaps in play.
   */
  rankSnapshots: AccountExplorerRankSnapshot[]
}

/** The request as the backend resolved it. */
export interface AccountExplorerQuery {
  gameName: string
  tagLine: string
  /** The requested platform id, or null when the search was region-wide. */
  region: string | null
}

/** The resolved account and the per-process freshness stamps. */
export interface AccountExplorerIdentity {
  riotAccountId: string
  puuid: string
  gameName: string
  tagLine: string | null
  platformId: string
  profileIconId: number
  summonerLevel: number
  /** `RiotAccountStatus` name: 'Active' or 'Invalid'. */
  status: string
  createdAtUtc: string
  updatedAtUtc: string
  /** Last successful account-v1 identity resolution. */
  lastProfileSyncAtUtc: string | null
  /** Last successful league-v4 read — stamped even when the rank was unchanged. */
  lastRankSyncAtUtc: string | null
  /** Can be newer than every main row's `calculatedAtUtc` — see `analysisSkipped`. */
  lastMainCalcAtUtc: string | null
  /** Last *successful* mastery check; a failed lookup leaves it untouched. */
  lastActivityCheckAtUtc: string | null
  lastMatchIngestAtUtc: string | null
  /** Rank sort key from the latest snapshot; `null` = never seen ranked, not 0. */
  rankScore: number | null
}

/** One of the other accounts sharing this Riot ID. */
export interface AccountExplorerAccountRef {
  riotAccountId: string
  puuid: string
  platformId: string
  status: string
  lastMatchIngestAtUtc: string | null
}

/**
 * Ingest-population membership and lease state. Every threshold that would turn
 * these into a verdict (claim lease, inactivity window, retained patch count) is
 * Ingestor config the API cannot see, so this section reports ages and stops —
 * it never claims a lease is stale. Judge `claimAgeSeconds` against
 * `MatchIngestion:ClaimLeaseMinutes` (30 by default) yourself.
 */
export interface AccountExplorerTracking {
  /** Derived, not a column: the two membership arms of the ingest claim. */
  isTracked: boolean
  trackedVia: 'EstablishedMain' | 'QueuedCandidate' | 'Both' | null
  hasActiveMain: boolean
  hasQueuedCandidate: boolean
  /** `MatchIngestStatus` name: 'Idle' or 'Processing'. */
  matchIngestStatus: string
  matchIngestClaimedAtUtc: string | null
  claimAgeSeconds: number | null
  lastMatchIngestAtUtc: string | null
  /** Claimable but its lease has never come up — the queue has not reached it. */
  neverIngested: boolean
}

/**
 * The three game counts that exist, each with the population it counts. They are
 * not three views of one number and must never be rendered as one: label each.
 */
export interface AccountExplorerMatchesIngested {
  /** Live participant rows: every champion, but bounded by retention. */
  liveParticipantCount: number
  /** Measured off the surviving rows, not derived from the retention config. */
  oldestRetainedGameStartUtc: string | null
  newestRetainedGameStartUtc: string | null
  /** Frozen aggregates: survive forever, but cover **main champions only**. */
  careerGamesFromAggregates: number
  aggregatedPatchCount: number
  /** A lower bound: a scope records its most recent game, not its first. */
  oldestAggregatedGameStartUtc: string | null
  /** Last MainAnalysis pass's sample size, capped at 50. A ceiling, not a total. */
  lastAnalysisSampleSize: number | null
  /**
   * True when the frozen aggregates prove games existed that the live rows no
   * longer hold. **False does not mean nothing was pruned** — the aggregates only
   * cover main champions. Render `prunedNote` either way; never show a bare 0.
   */
  pruned: boolean
  prunedNote: string
}

/** One `main_candidates` row and what the scorer had to work with. */
export interface AccountExplorerCandidate {
  id: string
  championId: number
  status: MainCandidateStatus
  /**
   * `MainCandidateSource` name. `ManualSeed` is never assigned in production —
   * ManualSeedProcess reuses the ladder upsert — so a manually seeded candidate
   * reads `Ladder`. Read `seedRequest` for the manual trail.
   */
  source: 'Ladder' | 'ManualSeed' | 'Harvest'
  score: number
  /**
   * The persisted inputs. The score's **components are not stored** — only the
   * final blend — so they cannot be shown, and recomputing them would mix today's
   * scarcity snapshot into a number produced against an older one.
   */
  scoreInputs: AccountExplorerCandidateScoreInputs
  discoveredAtUtc: string
  scoredAtUtc: string | null
  validatedAtUtc: string | null
}

/** Ladder candidates carry mastery rank/points; harvest ones carry observed games. */
export interface AccountExplorerCandidateScoreInputs {
  lastPlayTimeUtc: string
  championRankInMasteryTop: number
  championPoints: number
  observedGames: number
  /** Persisted but not a scoring input yet. */
  observedWins: number
}

export interface AccountExplorerMains {
  rows: AccountExplorerMainRow[]
  thresholds: AccountExplorerMainThresholds
}

/** The configured MainAnalysis thresholds a row's verdict should be read against. */
export interface AccountExplorerMainThresholds {
  /** Base play rate required for a well-covered champion (0.20). */
  playRateThreshold: number
  /** Lowest the adaptive threshold can drop to (0.12, #407). */
  playRateFloor: number
  otpPlayRateThreshold: number
  /** Below this, MainAnalysis refuses to overwrite an established main (#825). */
  minMatchesToEvaluate: number
  /** Why only a band is given. Render it next to the numbers. */
  effectiveThresholdNote: string
}

/** One `main_champion_stats` row. */
export interface AccountExplorerMainRow {
  championId: number
  /** The pass's sample size, not the account's total. */
  totalMatches: number
  championMatches: number
  playRate: number
  isMain: boolean
  isOtp: boolean
  /** A main only thanks to the coverage-relaxed floor (#407). */
  isExtendedSample: boolean
  isActive: boolean
  primaryPosition: string
  positionBreakdown: AccountExplorerPositionStat[]
  calculatedAtUtc: string
  /**
   * The last MainAnalysis run is newer than this row: the process looked and
   * declined to overwrite (thin-sample guard, #825). Not a stale-data bug.
   */
  analysisSkipped: boolean
  /** Null while active. */
  deactivation: AccountExplorerDeactivation | null
}

/** What is knowable about a retired main row — which is less than one would like. */
export interface AccountExplorerDeactivation {
  /**
   * The account's last *successful* mastery check. Null means the retirement was
   * never confirmed by a completed check, since a failed lookup leaves both the
   * flag and the stamp untouched.
   */
  confirmedByActivityCheckAtUtc: string | null
  /** Always false: there is no retirement-reason column. */
  reasonKnown: boolean
  /** The two causes the boolean collapses together, spelled out. Render it. */
  reasonNote: string
}

export interface AccountExplorerPositionStat {
  position: string
  games: number
  rate: number
}

/** One `rank_snapshots` row. */
export interface AccountExplorerRankSnapshot {
  capturedAtUtc: string
  tier: string
  division: string
  leaguePoints: number
  /** Null on snapshots taken before queue totals were recorded. */
  wins: number | null
  losses: number | null
}

// =============================================================================
// Effective configuration viewer — `GET /api/ops/configuration` (#1034)
// =============================================================================

/**
 * Where a bound value came from (#1034). `default` — no provider supplies the key,
 * the value is the class default. `override` — a provider supplies it; `source`
 * names which one. `derived` — no provider supplies it, yet the value differs from
 * the class default, so something computed it at boot.
 */
export type EffectiveConfigurationOrigin = 'default' | 'override' | 'derived'

/** How to read an effective-configuration value's number. */
export type EffectiveConfigurationUnit = 'bytes' | 'duration' | 'count' | 'percent' | 'flag' | 'list' | 'text'

/** One bound option, as the process holds it. */
export interface EffectiveConfigurationValue {
  /** Fully-qualified configuration key, e.g. `StorageHistory:DiskCapacityBytes`. */
  key: string
  /** The property name alone, e.g. `DiskCapacityBytes`. */
  name: string
  /** The pasteable-back-into-configuration form. Null when the option is unset. */
  value: string | null
  /** The humanised form ("90 days", "1.0 TB"), or null when it would repeat `value`. */
  valueLabel: string | null
  origin: EffectiveConfigurationOrigin
  /** Which provider supplied an override, e.g. `environment`. Null for `default`/`derived`. */
  source: string | null
  unit: EffectiveConfigurationUnit
  /** Set when the value is unset and that has a visible consequence elsewhere in the portal. */
  notice: string | null
}

/** One configuration section's worth of values, with the prose explaining what it drives. */
export interface EffectiveConfigurationSection {
  /** The configuration key prefix, e.g. `StorageHistory`. */
  name: string
  title: string
  description: string
  values: EffectiveConfigurationValue[]
}

/** One process's snapshot: which build, which environment, and its sections. */
export interface EffectiveConfigurationProcess {
  /** Which host bound these values — `Api` or `Ingestor`. */
  processName: string
  environment: string
  /** The build this process is running, or null for a plain local build. */
  version: string | null
  /**
   * When this snapshot was taken. For the Api this is always "now" — it is built
   * live on every request. For the Ingestor it is the boot time of its last run:
   * still what that process is running, even if older than the last deploy.
   */
  capturedAtUtc: string
  sections: EffectiveConfigurationSection[]
}

/** `GET /api/ops/configuration` — what every host is actually running with. */
export interface EffectiveConfigurationOverviewResponse {
  processes: EffectiveConfigurationProcess[]
}
