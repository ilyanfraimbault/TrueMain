import { POSITIONS, positionLabel } from '#common/utils/positions'
import type { ChampionPosition } from '#common/utils/positions'

/** One lane of the patch at a glance: its best pick, drawn over its art, and the next ones. */
export interface LanePicks<T> {
  lane: ChampionPosition
  label: string
  lead: T | null
  rest: T[]
}

/** The lanes in lane order, each cut to its `size` best entries (`laneEntries` is already ranked). */
export function lanePicks<T>(laneEntries: (lane: string) => T[], size = 5): LanePicks<T>[] {
  return POSITIONS.map((lane) => {
    const [lead, ...rest] = laneEntries(lane).slice(0, size)
    return { lane, label: positionLabel(lane), lead: lead ?? null, rest }
  })
}

/**
 * Skeletons only while nothing has landed: a lane left empty once the list is
 * in says "not enough games", and a failed load keeps its skeletons while it
 * retries rather than showing a false empty state.
 */
export function lanePicksLoading(columns: LanePicks<unknown>[], status: string): boolean {
  if (status === 'ready') return false
  return columns.every(column => column.lead === null)
}

/** The lanes and whether they are still loading, the one thing the dashboard reads. */
export function lanePicksView<T>(laneEntries: (lane: string) => T[], status: string) {
  const columns = lanePicks(laneEntries)
  return { columns, loading: lanePicksLoading(columns, status) }
}
