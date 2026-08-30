import { ALL } from './filters'

// Relative time-window filtering shared by the logs/processes/crashes panels:
// each window key maps to its duration, `SINCE_ITEMS` feeds the "Since"
// selects, and `sinceToIso` derives the ISO lower bound sent to the backend.
/**
 * The relative windows the "Since" selects offer, excluding `ALL`. Typed as a
 * closed union rather than `string` so `WINDOW_MS` is total: an unknown key
 * would otherwise yield `undefined`, and `sinceToIso` would build `Date(NaN)`
 * and throw a `RangeError` on `toISOString()`.
 */
export type TimeWindowKey = '1h' | '24h' | '7d' | '30d'

/** A "Since" select value: a relative window, or `ALL` for no lower bound. */
export type SinceWindow = typeof ALL | TimeWindowKey

export const WINDOW_MS: Record<TimeWindowKey, number> = {
  '1h': 60 * 60 * 1000,
  '24h': 24 * 60 * 60 * 1000,
  '7d': 7 * 24 * 60 * 60 * 1000,
  '30d': 30 * 24 * 60 * 60 * 1000,
}

export const SINCE_ITEMS: { label: string, value: SinceWindow }[] = [
  { label: 'All time', value: ALL },
  { label: 'Last hour', value: '1h' },
  { label: 'Last 24 hours', value: '24h' },
  { label: 'Last 7 days', value: '7d' },
  { label: 'Last 30 days', value: '30d' },
]

// ISO `since` lower bound for a relative (non-'all') window key.
export function sinceToIso(windowKey: TimeWindowKey): string {
  return new Date(Date.now() - WINDOW_MS[windowKey]).toISOString()
}
