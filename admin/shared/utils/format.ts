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
 * Humanize a duration in milliseconds (e.g. 1500 -> "1.5s", 90000 -> "1m 30s").
 * Sub-second durations render in ms; longer ones in s / m / h.
 */
export function formatDuration(ms: number | null | undefined): string {
  if (ms === null || ms === undefined || !Number.isFinite(ms) || ms < 0) {
    return '—'
  }
  if (ms < 1000) {
    return `${Math.round(ms)}ms`
  }
  const totalSeconds = Math.floor(ms / 1000)
  if (totalSeconds < 60) {
    // One decimal of sub-second precision for short runs.
    return `${(ms / 1000).toFixed(1)}s`
  }
  const minutes = Math.floor(totalSeconds / 60)
  const seconds = totalSeconds % 60
  if (minutes < 60) {
    return seconds ? `${minutes}m ${seconds}s` : `${minutes}m`
  }
  const hours = Math.floor(minutes / 60)
  const remMinutes = minutes % 60
  return remMinutes ? `${hours}h ${remMinutes}m` : `${hours}h`
}
