// `GET /api/ops/request-latency` (#2307): how long the Api took to answer each ops route,
// from the per-minute `request_latency_rollups` its middleware writes. p50/p95 are estimated
// from a fixed histogram of buckets and never exceed the measured max.

export type RequestLatencyWindow = '24h' | '7d'

export interface RouteLatencyPoint {
  binStartUtc: string
  count: number
  /** Null when the route was not called in the bin: a gap, not a zero. */
  p95Ms: number | null
}

export interface RouteLatency {
  /** The route template, e.g. `/ops/accounts/{puuid}`. */
  route: string
  method: string
  count: number
  p50Ms: number
  p95Ms: number
  meanMs: number
  maxMs: number
  timeouts: number
  serverErrors: number
  /** One point per `binHours` across the whole window, oldest first. */
  series: RouteLatencyPoint[]
}

export interface RequestLatency {
  window: RequestLatencyWindow
  sinceUtc: string
  generatedAtUtc: string
  binHours: number
  /** A route whose p95 is above this is slow. */
  slowThresholdMs: number
  retentionDays: number | null
  /** Null when nothing was ever recorded — not a quiet window. */
  oldestRetainedUtc: string | null
  /** Requests the Api measured but could not persist since it started. */
  droppedSinceStart: number
  /** Slowest p95 first. */
  routes: RouteLatency[]
}
