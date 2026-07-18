import { ALL } from './filters'

// Relative time-window filtering shared by the logs/processes/crashes panels:
// each window key maps to its duration, `SINCE_ITEMS` feeds the "Since"
// selects, and `sinceToIso` derives the ISO lower bound sent to the backend.
export const WINDOW_MS: Record<string, number> = {
  '1h': 60 * 60 * 1000,
  '24h': 24 * 60 * 60 * 1000,
  '7d': 7 * 24 * 60 * 60 * 1000,
  '30d': 30 * 24 * 60 * 60 * 1000,
}

export const SINCE_ITEMS = [
  { label: 'All time', value: ALL },
  { label: 'Last hour', value: '1h' },
  { label: 'Last 24 hours', value: '24h' },
  { label: 'Last 7 days', value: '7d' },
  { label: 'Last 30 days', value: '30d' },
]

// ISO `since` lower bound for a relative (non-'all') window key.
export function sinceToIso(windowKey: string): string {
  return new Date(Date.now() - WINDOW_MS[windowKey]!).toISOString()
}
