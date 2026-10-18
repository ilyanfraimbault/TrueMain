// Pure helpers behind the process-summary rendering. Process summaries are
// free-form JSON (flat scalar maps, nested objects, or arrays of per-platform
// rows), so both the Processes page and `ProcessSummaryView` need to classify a
// value before deciding how to display it. Kept here — free of Vue and of any
// component state — so the classification rules are testable in isolation and
// stay identical wherever a summary is rendered.
import { formatNumber } from './format'

/**
 * Whether a run's `summary` payload has anything worth rendering.
 *
 * `null`/`undefined`, an empty array and an empty object all count as "no
 * summary". A scalar summary (unusual but tolerated) counts as content unless
 * it is the empty string, so an empty payload shows "No summary recorded"
 * rather than a blank field.
 */
export function hasSummary(summary: unknown): boolean {
  if (summary === null || summary === undefined) {
    return false
  }
  if (Array.isArray(summary)) {
    return summary.length > 0
  }
  if (typeof summary === 'object') {
    return Object.keys(summary).length > 0
  }
  return summary !== ''
}

/**
 * Turn a payload key into a display label: split camelCase, collapse
 * separators, capitalize (e.g. `matches_ingested` -> "Matches ingested",
 * `queuedCandidates` -> "Queued Candidates").
 */
export function humanizeKey(key: string): string {
  return key
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .replace(/^./, char => char.toUpperCase())
    .trim()
}

/**
 * Strictly a `{}`-literal-shaped object: rejects arrays and exotic objects
 * (Date/Map/Set/RegExp/null-prototype). JSON-sourced summaries never contain
 * those, but the stricter guard keeps a changing data source from being
 * mis-detected as a tabular row or an object field list.
 */
export function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    && Object.getPrototypeOf(value) === Object.prototype
}

/**
 * A "scalar" is anything rendered as a single line of text rather than a nested
 * structure: primitives plus null/undefined.
 */
export function isScalar(value: unknown): boolean {
  return value === null || value === undefined || typeof value !== 'object'
}

/**
 * Render a scalar as display text: numbers get locale grouping, booleans read
 * Yes/No, absent values render as an em dash.
 */
export function formatScalar(value: unknown): string {
  if (value === null || value === undefined) {
    return '—'
  }
  if (typeof value === 'number') {
    return formatNumber(value)
  }
  if (typeof value === 'boolean') {
    return value ? 'Yes' : 'No'
  }
  return String(value)
}

/** The few lines of a run's summary worth reading without opening it. */
export interface SummaryRecap {
  /** The run's own explanation, when it recorded one (`reason`: why it skipped or did nothing). */
  reason: string | null
  /** Its first top-level counters, in the order the process wrote them. */
  fields: { label: string, value: string }[]
}

/**
 * A short recap of a free-form summary, for the iteration timeline and the bar's
 * hover card. Only top-level numbers count as fields: nested per-platform rows and
 * flags need the full view, which the run detail still opens.
 */
export function summaryRecap(summary: unknown, maxFields = 3): SummaryRecap {
  if (!isPlainObject(summary)) {
    return { reason: null, fields: [] }
  }
  const reason = typeof summary.reason === 'string' && summary.reason.trim() ? summary.reason.trim() : null
  const fields = Object.entries(summary)
    .filter(([, value]) => typeof value === 'number')
    .slice(0, maxFields)
    .map(([key, value]) => ({ label: humanizeKey(key), value: formatScalar(value) }))
  return { reason, fields }
}
