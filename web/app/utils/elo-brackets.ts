/**
 * Elo filter values used to scope champion builds / winrate by skill, mirroring
 * the backend `Core.Lol.Ranking.EloBracket`. A game is bucketed by the player's
 * ranked tier at game time (nearest rank snapshot to the match start).
 *
 * Each tier offers two forms: a cumulative "X+" threshold (`GOLD_PLUS` = "Gold
 * and above") and an exact single tier (`GOLD` = "Gold only"). The apex band
 * `MASTER_PLUS` has no separate exact form (Master / GM / Challenger are one
 * band). `ALL` is the default — no elo filter, the union of every band
 * (unranked included). Options are ordered highest rank → lowest, with the
 * cumulative form before the exact form within each tier.
 */
export type EloBracket =
  | 'ALL'
  | 'MASTER_PLUS'
  | 'DIAMOND_PLUS' | 'DIAMOND'
  | 'EMERALD_PLUS' | 'EMERALD'
  | 'PLATINUM_PLUS' | 'PLATINUM'
  | 'GOLD_PLUS' | 'GOLD'
  | 'SILVER_PLUS' | 'SILVER'
  | 'BRONZE_PLUS' | 'BRONZE'
  | 'IRON_PLUS' | 'IRON'

export const ELO_BRACKET_ALL: EloBracket = 'ALL'

/**
 * Selectable values, highest rank → lowest; within a tier the "+" (cumulative)
 * form precedes the exact form. `ALL` leads as the default.
 */
export const ELO_BRACKET_OPTIONS: Array<{ label: string, value: EloBracket }> = [
  { label: 'All ranks', value: 'ALL' },
  { label: 'Master+', value: 'MASTER_PLUS' },
  { label: 'Diamond+', value: 'DIAMOND_PLUS' },
  { label: 'Diamond', value: 'DIAMOND' },
  { label: 'Emerald+', value: 'EMERALD_PLUS' },
  { label: 'Emerald', value: 'EMERALD' },
  { label: 'Platinum+', value: 'PLATINUM_PLUS' },
  { label: 'Platinum', value: 'PLATINUM' },
  { label: 'Gold+', value: 'GOLD_PLUS' },
  { label: 'Gold', value: 'GOLD' },
  { label: 'Silver+', value: 'SILVER_PLUS' },
  { label: 'Silver', value: 'SILVER' },
  { label: 'Bronze+', value: 'BRONZE_PLUS' },
  { label: 'Bronze', value: 'BRONZE' },
  { label: 'Iron+', value: 'IRON_PLUS' },
  { label: 'Iron', value: 'IRON' },
]

export function isEloBracket(value: unknown): value is EloBracket {
  return typeof value === 'string' && ELO_BRACKET_OPTIONS.some(o => o.value === value)
}

/** Short human label for a threshold value (falls back to `All ranks`). */
export function eloBracketLabel(value: string | null | undefined): string {
  return ELO_BRACKET_OPTIONS.find(o => o.value === value)?.label ?? 'All ranks'
}
