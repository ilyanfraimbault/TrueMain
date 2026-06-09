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

export interface ProcessRunsResponse {
  runs: ProcessRun[]
  rollup: ProcessRollup[]
}

/** Filters for `GET /api/ops/process-runs`. */
export interface ProcessRunsFilters {
  processName?: string
  status?: ProcessRunStatus
  /** ISO datetime lower bound. */
  since?: string
  /** Defaults to 100 on the backend. */
  limit?: number
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
