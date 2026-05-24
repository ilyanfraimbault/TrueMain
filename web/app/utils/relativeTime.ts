// Single shared instance of Intl.RelativeTimeFormat — recreating it on each
// row is measurable on a long match-history feed. `en` is the only locale we
// surface today; expose `formatRelativeTime` with an optional locale arg if
// we ever localize.

const formatter = new Intl.RelativeTimeFormat('en', { numeric: 'auto' })

const SECOND = 1000
const MINUTE = 60 * SECOND
const HOUR = 60 * MINUTE
const DAY = 24 * HOUR
const WEEK = 7 * DAY
const MONTH = 30 * DAY
const YEAR = 365 * DAY

/**
 * Returns "8h ago", "3 days ago", "just now", etc. Negative deltas (future)
 * are rendered with the same formatter so a clock-skew edge case still
 * produces a readable string.
 */
export function formatRelativeTime(isoTimestamp: string, now: Date = new Date()): string {
  const then = new Date(isoTimestamp).getTime()
  if (Number.isNaN(then)) {
    return ''
  }

  const diffMs = then - now.getTime()
  const absDiff = Math.abs(diffMs)
  const sign = Math.sign(diffMs) as -1 | 0 | 1

  if (absDiff < MINUTE) {
    return formatter.format(Math.round(diffMs / SECOND), 'second')
  }
  if (absDiff < HOUR) {
    return formatter.format(Math.round(diffMs / MINUTE), 'minute')
  }
  if (absDiff < DAY) {
    return formatter.format(Math.round(diffMs / HOUR), 'hour')
  }
  if (absDiff < WEEK) {
    return formatter.format(Math.round(diffMs / DAY), 'day')
  }
  if (absDiff < MONTH) {
    return formatter.format(Math.round(diffMs / WEEK), 'week')
  }
  if (absDiff < YEAR) {
    return formatter.format(Math.round(diffMs / MONTH), 'month')
  }
  return formatter.format(Math.round(diffMs / YEAR), 'year')
}

/**
 * Formats a duration in seconds as <c>mm:ss</c>. Used for the game-duration
 * label on each match row.
 */
export function formatDuration(totalSeconds: number): string {
  const safe = Math.max(0, Math.floor(totalSeconds))
  const minutes = Math.floor(safe / 60)
  const seconds = safe % 60
  return `${minutes}:${seconds.toString().padStart(2, '0')}`
}
