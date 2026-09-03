import { getPositionIconUrl } from '~~/shared/utils/ddragon'

// Mirrors `web/app/utils/positions.ts` — the icon strip + the ALL sentinel
// used by the Champions filter and by the other tables that print a lane.
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
 * carries the position as a plain string, and sometimes an unknown one).
 */
export const POSITION_BY_VALUE: ReadonlyMap<string, typeof POSITION_OPTIONS[number]>
  = new Map(POSITION_OPTIONS.map(option => [option.value as string, option]))

export const ALL_POSITIONS_ICON_URL = getPositionIconUrl('fill')
