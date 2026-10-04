// Crash reports — `GET /api/ops/crashes`.

import type { LogLevel } from '../logs'

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
