import type { BadgeColor, ProcessHealthStatus } from '~~/shared/types/ops'
import { formatElapsed } from '~~/shared/utils/format'

/**
 * Presentation rules for the health cockpit (#1031) that need to be right rather than
 * merely to look right. Under `shared/` because the admin suite has no page-level
 * harness — logic left inline in the SFC could not be tested at all.
 *
 * The green/amber/red/unknown signal vocabulary lives in `detector-status.ts`; this file
 * covers the two things unique to the cockpit: run statuses and the signed pipeline gaps.
 */

/** One colour and one icon per run status — the status is dressed once. */
export interface ProcessStatusMeta {
  /** `UBadge`/`UIcon` colour for the status. */
  color: BadgeColor
  /** Lucide icon for the same status. */
  icon: string
}

/**
 * Colour *and* icon for a process's effective run status, in one table.
 *
 * Split across two private tables in `/processes` and `/health`, this had already
 * drifted: `Running` was `primary` on one page and `info` on the other, so
 * the same in-flight run was a different colour depending on where you looked at it.
 * `info` wins because `primary` is the portal's "this succeeded" colour and a run that is
 * still going has not succeeded yet.
 *
 * `Missing` is neutral, not a warning: a process that has never recorded a run is
 * unmeasured, and on a fresh environment every one of them is. `Abandoned` is a warning
 * rather than an error because the run's own outcome is unknown — its host died — which
 * is a different claim from "it ran and failed". `Skipped` is neutral for a third reason:
 * the run is settled and healthy, its cadence guard simply decided it was too early, so
 * it is neither a success to celebrate nor a problem to flag.
 *
 * `Skipped` and `Missing` share the neutral colour but never the icon: one ran and
 * declined, the other never started, and the badge has to carry the difference.
 */
export const PROCESS_STATUS_META: Record<ProcessHealthStatus, ProcessStatusMeta> = {
  Success: { color: 'success', icon: 'i-lucide-circle-check' },
  Failed: { color: 'error', icon: 'i-lucide-circle-x' },
  Running: { color: 'info', icon: 'i-lucide-loader-circle' },
  Abandoned: { color: 'warning', icon: 'i-lucide-circle-slash' },
  Skipped: { color: 'neutral', icon: 'i-lucide-skip-forward' },
  Missing: { color: 'neutral', icon: 'i-lucide-circle-dashed' },
}

/**
 * Meta for a run status, falling back to `Missing` for anything unrecognised — a status
 * the backend adds later must read as "no verdict", never as a success or a failure.
 */
export function processStatusMeta(status: ProcessHealthStatus | string): ProcessStatusMeta {
  return PROCESS_STATUS_META[status as ProcessHealthStatus] ?? PROCESS_STATUS_META.Missing
}

/** Colour for a process's effective run status. See {@link PROCESS_STATUS_META}. */
export function processStatusColor(status: ProcessHealthStatus | string): BadgeColor {
  return processStatusMeta(status).color
}

/** Icon for a process's effective run status. See {@link PROCESS_STATUS_META}. */
export function processStatusIcon(status: ProcessHealthStatus | string): string {
  return processStatusMeta(status).icon
}

/**
 * Formats a signed gap in minutes as a magnitude, e.g. `-90` and `90` both render
 * `"1h 30m"`. The sign carries meaning that differs per gap, so the caller words the
 * direction; this only sizes it.
 *
 * The tiers themselves are `formatElapsed`'s, not this function's: a private ladder here
 * is how a three-day span came to read `3d` on `/health` and `72h` on `/processes`, two
 * pages that cross-reference each other. Only the two things genuinely local to a gap
 * stay: the null wording and the minutes-to-ms conversion.
 *
 * `null` renders as an explicit "not measurable" rather than `0`: the backend returns null
 * when one side of the subtraction has nothing to measure, and printing that as a zero-
 * minute gap would claim the pipeline is perfectly caught up when it is in fact unmeasured.
 */
export function formatGapMagnitude(minutes: number | null | undefined): string {
  if (minutes === null || minutes === undefined || !Number.isFinite(minutes)) {
    return 'not measurable'
  }

  const total = Math.abs(Math.round(minutes))
  // A gap is measured in whole minutes, so one that rounds to zero is "under a minute",
  // not "under a millisecond": `formatElapsed`'s ms/s tiers would print a precision this
  // reading does not have.
  return total === 0 ? '0m' : formatElapsed(total * 60_000)
}

/**
 * Words the champion-data gap, whose sign is the opposite of what a reader expects: the
 * backend computes `newestMatch - lastAggregation`, so a *negative* value means the
 * aggregation ran after the newest match and is caught up.
 */
export function championDataLagLabel(minutes: number | null | undefined): string {
  if (minutes === null || minutes === undefined || !Number.isFinite(minutes)) {
    return 'not measurable'
  }
  return minutes <= 0
    ? `caught up (${formatGapMagnitude(minutes)} ahead)`
    : `${formatGapMagnitude(minutes)} behind`
}

/**
 * Words the ingestion→analysis gap. Here the backend computes
 * `mainAnalysisFinish - matchIngestionFinish`, so a *positive* value is the healthy
 * direction: analysis finished after the ingestion it consumes.
 */
export function ingestionToAnalysisLabel(minutes: number | null | undefined): string {
  if (minutes === null || minutes === undefined || !Number.isFinite(minutes)) {
    return 'not measurable'
  }
  return minutes >= 0
    ? `analysis ${formatGapMagnitude(minutes)} after ingestion`
    : `analysis ${formatGapMagnitude(minutes)} behind ingestion`
}
