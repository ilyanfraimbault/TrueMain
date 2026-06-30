/**
 * Coarse elo brackets used to scope champion builds by skill band, mirroring
 * the backend `Core.Lol.Ranking.EloBracket` constants. A game is bucketed by
 * the player's ranked tier at game time (nearest rank snapshot to the match
 * start). `ALL` is the default — the read-time union of every band.
 *
 * Buckets are deliberately broad so high brackets keep a usable sample:
 *   - Iron–Gold, Platinum–Emerald, Diamond+, Master+.
 */
export type EloBracket = 'ALL' | 'IRON_GOLD' | 'PLATINUM_EMERALD' | 'DIAMOND_PLUS' | 'MASTER_PLUS'

export const ELO_BRACKET_ALL: EloBracket = 'ALL'

/** Selectable brackets, broadest → narrowest; `ALL` leads as the default. */
export const ELO_BRACKET_OPTIONS: Array<{ label: string, value: EloBracket }> = [
  { label: 'All ranks', value: 'ALL' },
  { label: 'Iron–Gold', value: 'IRON_GOLD' },
  { label: 'Plat–Emerald', value: 'PLATINUM_EMERALD' },
  { label: 'Diamond+', value: 'DIAMOND_PLUS' },
  { label: 'Master+', value: 'MASTER_PLUS' },
]

export function isEloBracket(value: unknown): value is EloBracket {
  return typeof value === 'string' && ELO_BRACKET_OPTIONS.some(o => o.value === value)
}

/** Short human label for a bracket value (falls back to the raw value). */
export function eloBracketLabel(value: string | null | undefined): string {
  return ELO_BRACKET_OPTIONS.find(o => o.value === value)?.label ?? 'All ranks'
}
