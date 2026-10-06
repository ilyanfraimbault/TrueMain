// Riot quota utilisation per routing host and lane duty cycle — `GET /api/ops/riot-quota` (#1458).
import type { RiotBindingLimit, RiotUsageWindow } from './riot-usage'

/** `regional` hosts serve account-v1 and match-v5; `platform` hosts everything else. Separate budgets. */
export type RiotRouteKind = 'regional' | 'platform' | 'unknown'

/** One caller × endpoint spending a host's budget. `share` is of the host's calls, in [0, 1]. */
export interface RiotRouteConsumer {
  caller: string
  endpoint: string
  calls: number
  rateLimited: number
  share: number
}

/**
 * One routing host's budget over the covered span. `calls` counts every physical attempt,
 * 429s included. `utilisation`/`currentUtilisation`/`bindingLimit` are null when the host
 * returned no parseable `X-App-Rate-Limit` in the window — no limit, no ratio.
 */
export interface RiotRouteQuota {
  route: string
  kind: RiotRouteKind
  calls: number
  rateLimited: number
  /** 429s / calls; null when there were no calls. */
  rateLimitedRate: number | null
  errors: number
  callsPerMinute: number
  /** Share of the covered minutes with at least one call to this host. */
  activeMinuteShare: number
  bindingLimit: RiotBindingLimit | null
  /** Calls over the covered span / what the binding limit allows over it. */
  utilisation: number | null
  /** Freshest `X-App-Rate-Limit-Count` on the binding window / its limit, at `observedAtUtc`. */
  currentUtilisation: number | null
  appRateLimit: string | null
  appRateLimitCount: string | null
  observedAtUtc: string | null
  consumers: RiotRouteConsumer[]
}

export interface ProcessDutyCycle {
  processName: string
  dutyCycle: number
  busyHours: number
  runs: number
}

/** Share of wall-clock time at least one of the lane's processes was running, in [0, 1]. */
export interface LaneDutyCycle {
  lane: string
  dutyCycle: number
  busyHours: number
  runs: number
  processes: ProcessDutyCycle[]
}

/** `GET /api/ops/riot-quota` — per-host quota utilisation and lane duty cycle over a window. */
export interface RiotQuota {
  window: RiotUsageWindow
  sinceUtc: string
  generatedAtUtc: string
  /** `sinceUtc`, or the oldest retained rollup when the retention ends inside the window. */
  coverageStartUtc: string
  coveredHours: number
  /** Configured rollup retention in days; null when the TTL is disabled. */
  retentionDays: number | null
  oldestRetainedUtc: string | null
  /** Regional hosts first, then platform hosts, each by calls descending. */
  routes: RiotRouteQuota[]
  lanes: LaneDutyCycle[]
}
