import type { BadgeColor, ProcessHealthStatus } from '~~/shared/types/ops'

/**
 * Presentation rules for the health cockpit (#1031) that need to be right rather than
 * merely to look right. Under `shared/` because the admin suite has no page-level
 * harness — logic left inline in the SFC could not be tested at all.
 *
 * The green/amber/red/unknown signal vocabulary lives in `detector-status.ts`; this file
 * covers the two things unique to the cockpit: run statuses and the signed pipeline gaps.
 */

/**
 * Colour for a process's effective run status.
 *
 * `Missing` is neutral, not a warning: a process that has never recorded a run is
 * unmeasured, and on a fresh environment every one of them is. `Abandoned` is a warning
 * rather than an error because the run's own outcome is unknown — its host died — which
 * is a different claim from "it ran and failed".
 */
export function processStatusColor(status: ProcessHealthStatus | string): BadgeColor {
  switch (status) {
    case 'Success':
      return 'success'
    case 'Failed':
      return 'error'
    case 'Abandoned':
      return 'warning'
    case 'Running':
      return 'info'
    default:
      return 'neutral'
  }
}

/**
 * Formats a signed gap in minutes as a magnitude, e.g. `-90` and `90` both render
 * `"1h 30m"`. The sign carries meaning that differs per gap, so the caller words the
 * direction; this only sizes it.
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
  if (total < 60) {
    return `${total}m`
  }

  const hours = Math.floor(total / 60)
  const remMinutes = total % 60
  if (hours < 24) {
    return remMinutes ? `${hours}h ${remMinutes}m` : `${hours}h`
  }

  const days = Math.floor(hours / 24)
  const remHours = hours % 24
  return remHours ? `${days}d ${remHours}h` : `${days}d`
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
