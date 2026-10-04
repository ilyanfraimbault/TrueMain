// Ingestor process runs and pipeline iterations — `GET /api/ops/process-runs`, `/process-iterations`.

/**
 * `Abandoned` is a run that started but never recorded an outcome: its owning ingestor
 * died mid-flight — assigned at startup (orphaned `Running` rows are reconciled) or on
 * read (a stale heartbeat). Terminal, and distinct from `Failed`. `Cancelled` is its
 * counterpart: the same interruption, but a host that stopped because it was asked to.
 *
 * `Skipped` is a run whose cadence guard decided this iteration was too early (e.g.
 * `Discovery:MinRunInterval`). Settled and healthy — but it did no work, so it is
 * deliberately not a `Success`: the backend excludes it from the cadence gate that reads
 * "when did this last actually run?" and from the consecutive-failure streak, which
 * `Cancelled` also stays out of — though unlike a skip it left work unfinished.
 */
export type ProcessRunStatus = 'Success' | 'Failed' | 'Running' | 'Abandoned' | 'Skipped' | 'Cancelled'

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
  /**
   * The `Job:Mode` the pass was running (`Full`, `FetchLane`, `AggregateLane`, or a
   * single-process mode), or null for runs recorded before it was captured.
   *
   * The lane a run belongs to is derived from its process name, so this is not what
   * groups the chain. What it answers is the question the process names cannot: what
   * the pass was *supposed* to run. A one-off single-process mode looks exactly like a
   * lane that has barely started, and only this tells them apart.
   */
  jobMode: string | null
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
  /** The `Job:Mode` this pass ran, or null for passes recorded before it was captured. */
  jobMode: string | null
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
