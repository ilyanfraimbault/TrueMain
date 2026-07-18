import { getPositionIconUrl } from '~~/shared/utils/ddragon'

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
