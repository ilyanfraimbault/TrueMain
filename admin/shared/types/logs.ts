// Types for the admin Logs panel (`GET /api/ops/logs`), split out of `ops.ts`,
// which is over the file-size limit and may only shrink.

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
  /**
   * The request the row was written during (#1555), null outside a request.
   * `traceId` is the id the client got back in its ProblemDetails.
   */
  traceId: string | null
  requestMethod: string | null
  /** Path, with its query when the writer included it. */
  requestPath: string | null
  statusCode: number | null
  durationMs: number | null
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
