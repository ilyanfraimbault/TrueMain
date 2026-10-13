import { getPositionIconUrl } from '#shared/utils/ddragon'

/** The five positions, in lane order (top → support): every list of them reads in this order. */
export const POSITIONS = ['TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY'] as const

export type ChampionPosition = (typeof POSITIONS)[number]

export interface PositionOption {
  value: ChampionPosition
  /** The position's name: `Middle`, `Bottom`. */
  label: string
  /** The short form for tight rows (the desktop app's draft, game and recap screens): `Mid`, `Bot`. */
  shortLabel: string
  iconUrl: string
}

export const POSITION_OPTIONS: PositionOption[] = [
  { label: 'Top', shortLabel: 'Top', value: 'TOP', iconUrl: getPositionIconUrl('TOP') },
  { label: 'Jungle', shortLabel: 'Jungle', value: 'JUNGLE', iconUrl: getPositionIconUrl('JUNGLE') },
  { label: 'Middle', shortLabel: 'Mid', value: 'MIDDLE', iconUrl: getPositionIconUrl('MIDDLE') },
  { label: 'Bottom', shortLabel: 'Bot', value: 'BOTTOM', iconUrl: getPositionIconUrl('BOTTOM') },
  { label: 'Support', shortLabel: 'Support', value: 'UTILITY', iconUrl: getPositionIconUrl('UTILITY') },
]

/**
 * POSITION_OPTIONS keyed by value, for O(1) lookups from row data (which
 * carries the position as a plain string).
 */
export const POSITION_BY_VALUE: ReadonlyMap<string, PositionOption>
  = new Map(POSITION_OPTIONS.map(option => [option.value as string, option]))

export function isChampionPosition(value: unknown): value is ChampionPosition {
  return typeof value === 'string' && POSITION_BY_VALUE.has(value)
}

/**
 * A position's name, `short` for the tight rows' form; null for a string that is
 * no position (an empty slot, a remake's blank), so the caller decides what stands in.
 */
export function positionLabel(position: ChampionPosition, options?: { short?: boolean }): string
export function positionLabel(position: string | null | undefined, options?: { short?: boolean }): string | null
export function positionLabel(position: string | null | undefined, options: { short?: boolean } = {}): string | null {
  const option = position ? POSITION_BY_VALUE.get(position) : undefined
  if (!option) return null
  return options.short ? option.shortLabel : option.label
}

/** A position's place in lane order; a missing or unknown one sorts last. */
export function positionIndex(position: string | null | undefined): number {
  const index = position ? (POSITIONS as readonly string[]).indexOf(position) : -1
  return index === -1 ? POSITIONS.length : index
}

/**
 * Sort comparator for one team's players in lane order (TOP → SUPPORT), so two
 * teams sorted with it pair laner against laner. A missing or unknown position
 * goes last and otherwise keeps its original order.
 */
export function compareByPosition(a: { position: string | null }, b: { position: string | null }): number {
  return positionIndex(a.position) - positionIndex(b.position)
}
