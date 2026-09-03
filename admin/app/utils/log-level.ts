import type { BadgeColor, LogLevel } from '~~/shared/types/ops'

/**
 * Severity names ordered from least to most severe — the level filter's option
 * order, and the catalogue the `?level=` deep link is matched against.
 */
export const LOG_LEVELS: LogLevel[] = [
  'Trace',
  'Debug',
  'Information',
  'Warning',
  'Error',
  'Critical',
]

/**
 * The Logs page opens on Warning and above (#1415): the operator lands there to
 * see what failed, and `Information` rows drown that out on first paint. The
 * backend's `level` filter is a minimum threshold, so this one value asks for
 * Warning + Error + Critical. "All levels" stays one click away.
 */
export const DEFAULT_LOG_LEVEL: LogLevel = 'Warning'

/**
 * Reads a `?level=` deep link. Accepts an exact `LogLevel` name
 * (case-insensitive) or the `'all'` sentinel; anything else — a missing or
 * bogus param included — falls back to the errors-first default.
 */
export function parseLevelQuery(raw: unknown): 'all' | LogLevel {
  const value = Array.isArray(raw) ? raw[0] : raw
  if (typeof value !== 'string') {
    return DEFAULT_LOG_LEVEL
  }
  const normalized = value.trim().toLowerCase()
  if (normalized === 'all') {
    return 'all'
  }
  return LOG_LEVELS.find(level => level.toLowerCase() === normalized)
    ?? DEFAULT_LOG_LEVEL
}

// Severity presentation for log rows, shared by the Logs page and the crash
// report's recent-log-tail view.
export function levelColor(l: LogLevel): BadgeColor {
  switch (l) {
    case 'Critical':
    case 'Error':
      return 'error'
    case 'Warning':
      return 'warning'
    case 'Information':
      return 'success'
    default:
      // Debug / Trace — muted.
      return 'neutral'
  }
}

export function levelIcon(l: LogLevel): string {
  switch (l) {
    case 'Critical':
      return 'i-lucide-octagon-alert'
    case 'Error':
      return 'i-lucide-circle-x'
    case 'Warning':
      return 'i-lucide-triangle-alert'
    case 'Information':
      return 'i-lucide-info'
    default:
      return 'i-lucide-bug'
  }
}
