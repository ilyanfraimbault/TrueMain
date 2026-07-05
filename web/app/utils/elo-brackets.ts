/**
 * Per-tier elo filters used to scope champion builds by rank, mirroring the
 * backend `Core.Lol.Ranking.EloBracket`. A game is bucketed by the player's
 * ranked tier at game time (nearest rank snapshot to the match start).
 *
 * A filter value is one of:
 *   - `ALL` — every tier (the default).
 *   - a bare tier (e.g. `GOLD`) — that tier only.
 *   - a `<TIER>_PLUS` form (e.g. `GOLD_PLUS`) — that tier and every tier above.
 */

/** Ranked tiers, ascending (Iron→Master). Master folds in GM/Challenger. */
export const ELO_TIERS = [
  'IRON',
  'BRONZE',
  'SILVER',
  'GOLD',
  'PLATINUM',
  'EMERALD',
  'DIAMOND',
  'MASTER',
] as const

export type EloTier = typeof ELO_TIERS[number]

export const ELO_BRACKET_ALL = 'ALL'
export const ELO_PLUS_SUFFIX = '_PLUS'

/** Filter value for "this tier only". */
export function tierOnly(tier: EloTier): string {
  return tier
}

/** Filter value for "this tier and above". */
export function tierPlus(tier: EloTier): string {
  return `${tier}${ELO_PLUS_SUFFIX}`
}

/** Master tops the ladder, so its "+" would add nothing — hide it. */
export function hasPlus(tier: EloTier): boolean {
  return tier !== 'MASTER'
}

export function isEloTier(value: unknown): value is EloTier {
  return typeof value === 'string' && (ELO_TIERS as readonly string[]).includes(value)
}

/** True for `ALL`, a bare tier, or a recognised `<TIER>_PLUS` form. */
export function isEloBracket(value: unknown): boolean {
  if (typeof value !== 'string') return false
  if (value === ELO_BRACKET_ALL) return true
  const tier = value.endsWith(ELO_PLUS_SUFFIX) ? value.slice(0, -ELO_PLUS_SUFFIX.length) : value
  return isEloTier(tier)
}

/** Canonicalise to a recognised filter, falling back to `ALL`. */
export function normalizeEloBracket(value: string | null | undefined): string {
  if (!value) return ELO_BRACKET_ALL
  const upper = value.toUpperCase()
  return isEloBracket(upper) ? upper : ELO_BRACKET_ALL
}

/** Human label, e.g. `Gold`, `Gold+`, `All ranks`. */
export function eloBracketLabel(value: string | null | undefined): string {
  const filter = normalizeEloBracket(value)
  if (filter === ELO_BRACKET_ALL) return 'All ranks'
  const andAbove = filter.endsWith(ELO_PLUS_SUFFIX)
  const tier = andAbove ? filter.slice(0, -ELO_PLUS_SUFFIX.length) : filter
  const label = tier.charAt(0) + tier.slice(1).toLowerCase()
  return andAbove ? `${label}+` : label
}
