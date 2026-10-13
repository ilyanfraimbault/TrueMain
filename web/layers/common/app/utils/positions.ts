import { getPositionIconUrl } from '#shared/utils/ddragon'

export type ChampionPosition = 'TOP' | 'JUNGLE' | 'MIDDLE' | 'BOTTOM' | 'UTILITY'

export const POSITION_OPTIONS: Array<{ label: string, value: ChampionPosition, iconUrl: string }> = [
  { label: 'Top', value: 'TOP', iconUrl: getPositionIconUrl('TOP') },
  { label: 'Jungle', value: 'JUNGLE', iconUrl: getPositionIconUrl('JUNGLE') },
  { label: 'Middle', value: 'MIDDLE', iconUrl: getPositionIconUrl('MIDDLE') },
  { label: 'Bottom', value: 'BOTTOM', iconUrl: getPositionIconUrl('BOTTOM') },
  { label: 'Support', value: 'UTILITY', iconUrl: getPositionIconUrl('UTILITY') },
]

/**
 * POSITION_OPTIONS keyed by value, for O(1) lookups from row data (which
 * carries the position as a plain string).
 */
export const POSITION_BY_VALUE: ReadonlyMap<string, typeof POSITION_OPTIONS[number]>
  = new Map(POSITION_OPTIONS.map(option => [option.value as string, option]))

export function isChampionPosition(value: unknown): value is ChampionPosition {
  return typeof value === 'string' && POSITION_OPTIONS.some(o => o.value === value)
}

function positionIndex(position: string | null | undefined): number {
  const index = POSITION_OPTIONS.findIndex(o => o.value === position)
  return index === -1 ? POSITION_OPTIONS.length : index
}

/**
 * Sort comparator for one team's players in lane order (TOP → SUPPORT), so two
 * teams sorted with it pair laner against laner. A missing or unknown position
 * goes last and otherwise keeps its original order.
 */
export function compareByPosition(a: { position: string | null }, b: { position: string | null }): number {
  return positionIndex(a.position) - positionIndex(b.position)
}
