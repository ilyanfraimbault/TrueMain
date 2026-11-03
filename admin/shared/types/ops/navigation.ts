import type { DetectorStatus } from './common'

/**
 * `GET /ops/nav-summary` (#2294) — what the sidebar flags: the queues waiting for
 * the operator and the cockpit's verdict.
 */
export interface NavSummary {
  /** Open known-player account conflicts. */
  openConflicts: number
  /** Homonym groups still holding a pair nobody merged or marked. */
  homonymGroups: number
  countedAtUtc: string
  /** The warm cockpit verdict; `unknown` too when none recent enough is held. */
  healthStatus: DetectorStatus
  /** Null when no recent evaluation is held: the server never runs one for this read. */
  healthEvaluatedAtUtc: string | null
}
