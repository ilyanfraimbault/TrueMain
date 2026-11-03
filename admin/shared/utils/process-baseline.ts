// Wording of a run's baseline (#2298): how far a value sits from the median of the
// recent successful runs of its process and mode. The thresholds live in the API
// (`ProcessRunBaselinePolicy`); the admin only reads `flagged`.
import type { ProcessRunBaseline, ProcessRunMetricBaseline } from '~~/shared/types/ops'

/** `+180%` / `−60%`, rounded to the percent; an em dash when the gap is undefined. */
export function formatDeviation(deviation: number | null): string {
  if (deviation === null) {
    return '—'
  }
  const percent = Math.round(deviation * 100)
  if (percent > 0) {
    return `+${percent.toLocaleString('en-US')}%`
  }
  return percent < 0 ? `−${Math.abs(percent).toLocaleString('en-US')}%` : '0%'
}

/** The flagged metric, or null — the only case a badge is drawn. */
export function flaggedMetric(metric: ProcessRunMetricBaseline | null | undefined): ProcessRunMetricBaseline | null {
  return metric?.flagged ? metric : null
}

/** The names of the summary counters that crossed a threshold, in summary order. */
export function flaggedCounters(baseline: ProcessRunBaseline | null | undefined): string[] {
  if (!baseline) {
    return []
  }
  return Object.entries(baseline.counters)
    .filter(([, metric]) => metric.flagged)
    .map(([name]) => name)
}
