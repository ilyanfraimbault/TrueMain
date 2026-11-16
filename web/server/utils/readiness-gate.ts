/** Failed checks in a row that make the stack count as down. */
export const READINESS_FAILS = 3

export interface ReadinessGate {
  /** Takes one check's result and says whether the stack counts as up after it. */
  record: (up: boolean) => boolean
}

/**
 * Consecutive failures, which the edge cannot count (#2416): Caddy's `health_fails`
 * adds failed probes up over the life of the process and a pass does not reset it, so
 * three slow probes days apart took the whole site offline. A fresh process starts as
 * down and counts as up from the API's first answer, so a rollout that brings web back
 * before the API does not flip the site back too early.
 */
export function createReadinessGate(fails = READINESS_FAILS): ReadinessGate {
  let failedInARow = fails
  return {
    record(up) {
      failedInARow = up ? 0 : failedInARow + 1
      return failedInARow < fails
    },
  }
}

/**
 * What the API answers while its first warm pass runs: a health check's `Degraded`, on a
 * `503` (`ChampionWarmupHealthCheck`). A failure reads `Unhealthy`, or does not read at all.
 */
export function isApiWarming(status: number | null, body: unknown): boolean {
  return status === 503 && body === 'Degraded'
}
