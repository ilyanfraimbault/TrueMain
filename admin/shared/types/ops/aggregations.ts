// Aggregation families and backlogs — `GET /api/ops/stats/aggregations`.

/**
 * One aggregation family from `GET /api/ops/stats/aggregations` — a group of
 * aggregate tables produced by a single ingestor process (builds patterns,
 * matchups, synergies, mains).
 */
export interface AggregationFamily {
  /** Stable identifier: "builds" | "matchups" | "synergies" | "mains". */
  key: string
  /** The recorded ingestor process producing this family. */
  processName: string
  /**
   * The family's tables with the planner's live-row estimate of each (#2303) — the
   * figure `/database` prints, approximate by nature, so the page shows it with `≈`.
   */
  tables: { table: string, rowEstimate: number }[]
  /** Sum of the tables' estimates. */
  totalRowEstimate: number
  /** Distinct champions with rows, exact; for mains, champions with an active main. */
  distinctChampions: number
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
  /**
   * Queue-scoped matches not yet folded into the synergy aggregates. Starts at the
   * full retained match count on the first deploy (the fold flag ships false for
   * every existing row on purpose) and drains over the following runs.
   */
  pendingSynergyMatches: number
  /** Tracked participants still missing their elo bracket stamp. */
  pendingEloBracketParticipants: number
}

/** `GET /api/ops/stats/aggregations` — the Aggregation panel payload. */
export interface AggregationsResponse {
  queueId: number
  /** When the payload was measured — the API serves it from a two-minute cache. */
  measuredAtUtc: string
  families: AggregationFamily[]
  backlog: AggregationBacklog
}
