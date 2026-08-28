// Pure formatting helpers shared across the admin dashboard. Kept under
// `shared/` so both the Nitro server layer and the client can import them.

const BYTE_UNITS = ['B', 'KB', 'MB', 'GB', 'TB', 'PB'] as const

/**
 * Humanize a raw byte count into a binary-prefixed string (KB/MB/GB/…).
 *
 * Uses 1024 as the base (KiB semantics) but the conventional KB/MB/GB labels,
 * matching how Postgres `pg_size_pretty`-style reporting is usually read by
 * operators. Returns `"0 B"` for zero/negative/non-finite input so an empty
 * stat never renders as `NaN`.
 */
export function humanizeBytes(bytes: number, digits = 1): string {
  if (!Number.isFinite(bytes) || bytes <= 0) {
    return '0 B'
  }

  const exponent = Math.min(
    Math.floor(Math.log(bytes) / Math.log(1024)),
    BYTE_UNITS.length - 1,
  )
  const value = bytes / 1024 ** exponent
  // Whole bytes never need a fractional part; larger units round to `digits`.
  const formatted = exponent === 0
    ? String(Math.round(value))
    : value.toFixed(digits)
  return `${formatted} ${BYTE_UNITS[exponent]}`
}

/**
 * Format an integer with locale grouping (e.g. 1234567 -> "1,234,567").
 * `null`/`undefined`/non-finite render as an em dash so absent metrics read as
 * "no data" rather than "0".
 */
export function formatNumber(value: number | null | undefined): string {
  if (value === null || value === undefined || !Number.isFinite(value)) {
    return '—'
  }
  return Number(value).toLocaleString('en-US')
}

/**
 * Format an ISO datetime string as a compact, locale-aware date+time
 * (e.g. "Jun 9, 2026, 14:32"). `null`/empty renders as an em dash.
 */
export function formatDateTime(iso: string | null | undefined): string {
  if (!iso) {
    return '—'
  }
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) {
    return '—'
  }
  return date.toLocaleString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  })
}

/**
 * Format an ISO date as a short day label (e.g. "Jun 9"), for chart axes where
 * the year is implied by the surrounding window. Locale is pinned to `en-US`:
 * the admin copy is English, and an implicit browser locale would reshape the
 * axis per operator. `null`/invalid renders as an em dash.
 */
export function formatDayLabel(iso: string | null | undefined): string {
  if (!iso) {
    return '—'
  }
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) {
    return '—'
  }
  return date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
}

/**
 * Format an ISO date as a full, year-qualified date (e.g. "Jun 9, 2026"), for
 * standalone dates that are not read against a chart's own window. Same pinned
 * `en-US` locale as {@link formatDayLabel}.
 */
export function formatDate(iso: string | null | undefined): string {
  if (!iso) {
    return '—'
  }
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) {
    return '—'
  }
  return date.toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' })
}

/**
 * Humanize a duration in milliseconds (e.g. 1500 -> "1.5s", 90000 -> "1m 30s",
 * 259_200_000 -> "3d"). Sub-second durations render in ms; longer ones climb the
 * s / m / h / d ladder.
 *
 * Named `formatElapsed` rather than `formatDuration` because the web app exports a
 * `formatDuration` of its own meaning something else entirely — a `mm:ss` game clock.
 * One name with two contracts across two apps in one repo is a copy-paste away from a
 * wrong display that no type error catches.
 *
 * This is the portal's single duration ladder: `formatGapMagnitude` in
 * `pipeline-health.ts` delegates here, so a three-day span cannot read `72h` on one
 * page and `3d` on another.
 */
export function formatElapsed(ms: number | null | undefined): string {
  if (ms === null || ms === undefined || !Number.isFinite(ms) || ms < 0) {
    return '—'
  }
  if (ms < 1000) {
    return `${Math.round(ms)}ms`
  }
  // Round to the precision each tier actually prints *before* choosing the tier.
  // Picking the branch on the raw value while printing a rounded one is how
  // 59_999 ms used to render "60.0s" — a duration the next tier calls "1m".
  const tenthsOfSecond = Math.round(ms / 100)
  if (tenthsOfSecond < 600) {
    // One decimal of sub-second precision for short runs.
    return `${(tenthsOfSecond / 10).toFixed(1)}s`
  }
  const totalSeconds = Math.round(ms / 1000)
  const minutes = Math.floor(totalSeconds / 60)
  const seconds = totalSeconds % 60
  if (minutes < 60) {
    return seconds ? `${minutes}m ${seconds}s` : `${minutes}m`
  }
  const hours = Math.floor(minutes / 60)
  const remMinutes = minutes % 60
  if (hours < 24) {
    return remMinutes ? `${hours}h ${remMinutes}m` : `${hours}h`
  }
  // Days matter: an operator reading "72h" has to do the division before knowing
  // whether something has been stuck for an afternoon or for three days.
  const days = Math.floor(hours / 24)
  const remHours = hours % 24
  return remHours ? `${days}d ${remHours}h` : `${days}d`
}

/**
 * Format a 0-1 ratio as a percentage (0.1234 -> "12.3%").
 *
 * Takes the ratio rather than the already-scaled number because that is the shape
 * every rate on the wire has. Use it only for a value that really was measured —
 * `formatPercentOrDash` is the one that knows what to do when it wasn't.
 */
export function formatPercent(value: number, digits = 1): string {
  return `${(value * 100).toFixed(digits)}%`
}

/**
 * Percentage of a ratio the backend may not know, rendered as an em dash when it is
 * absent. "Not observed" and "observed at 0%" are different answers, and a fabricated
 * `0%` reads as the second one — the dashboard claiming a measurement it never made.
 * Non-finite input takes the same path: `NaN%` is not a reading either.
 */
export function formatPercentOrDash(value: number | null | undefined, digits = 1): string {
  return value === null || value === undefined || !Number.isFinite(value)
    ? '—'
    : formatPercent(value, digits)
}

/**
 * Compact relative-time label for an ISO timestamp (e.g. "3m ago", "2h ago",
 * "5d ago"). Sub-minute renders as "just now"; null/invalid as an em dash.
 * Freshness cue only — pair it with `formatDateTime` for the exact instant.
 */
export function formatTimeAgo(iso: string | null | undefined): string {
  if (!iso) {
    return '—'
  }
  const timestamp = new Date(iso).getTime()
  if (Number.isNaN(timestamp)) {
    return '—'
  }
  const elapsedMs = Date.now() - timestamp
  if (elapsedMs < 60_000) {
    return 'just now'
  }
  const minutes = Math.floor(elapsedMs / 60_000)
  if (minutes < 60) {
    return `${minutes}m ago`
  }
  const hours = Math.floor(minutes / 60)
  if (hours < 24) {
    return `${hours}h ago`
  }
  const days = Math.floor(hours / 24)
  return `${days}d ago`
}
