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
export type MatchTimeGranularity = 'week' | 'month' | 'year' | 'patch'

/**
 * One bucket of `GET /api/ops/stats/matches-over-time` (returned in chronological
 * order). Matches are counted by GAME date (`Match.GameStartTimeUtc`).
 *
 * `bucket` shape depends on the requested granularity:
 *   - week/month/year: ISO-8601 UTC timestamp of the period start
 *     (e.g. `2026-06-01T00:00:00Z`) — format the label client-side per granularity.
 *   - patch: the normalized `MAJOR.MINOR` version string (e.g. `16.4`) — use as-is.
 */
export interface MatchTimeBucket {
  bucket: string
  matches: number
}

/** One row of `GET /api/ops/db/tables` (sorted by `totalBytes` desc). */
export interface DbTableRow {
  tableName: string
  rowEstimate: number
  totalBytes: number
  tableBytes: number
  indexBytes: number
}

export type ProcessRunStatus = 'Success' | 'Failed'

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
  summary: Record<string, unknown> | unknown[] | null
}

/** One rollup row of `GET /api/ops/process-runs` → `rollup`. */
export interface ProcessRollup {
  processName: string
  lastStatus: ProcessRunStatus
  lastRunAtUtc: string
  lastSuccessAtUtc: string | null
  failureCountInWindow: number
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
}

/** `GET /api/ops/logs` — server-paginated log entries. */
export interface LogsResponse {
  entries: LogEntry[]
  /** Total rows matching the filters (across all pages). */
  total: number
  page: number
  pageSize: number
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
  /** 1-based page index. */
  page?: number
  /** Rows per page; backend clamps to [1, 200], default 50. */
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
}

/** Filters for `GET /api/ops/accounts/seed`. Empty/undefined = no filter. */
export interface SeedRequestsFilters {
  /** A `SeedRequestStatus` name. */
  status?: SeedRequestStatus
  /** Case-insensitive substring match on the Riot ID (gameName/tagLine). */
  search?: string
  /** Rows to return; backend default 50, clamp 200. */
  limit?: number
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

/** Nuxt UI badge/icon color for an issue type's severity. */
export type BadgeColor = 'error' | 'warning' | 'info' | 'neutral'

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
