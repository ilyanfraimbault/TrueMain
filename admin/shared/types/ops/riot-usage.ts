// Riot API usage, rate limits and headroom — `GET /api/ops/riot-usage`.

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
