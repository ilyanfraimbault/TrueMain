/**
 * Per-tier elo filters used to scope champion builds by rank, mirroring the
 * backend `Core.Lol.Ranking.EloBracket`. A game is bucketed by the player's
 * ranked tier at game time (nearest rank snapshot to the match start).
 *
 * A filter value is one of:
 *   - `ALL` — every tier.
 *   - a bare tier (e.g. `GOLD`) — that tier only.
 *   - a `<TIER>_PLUS` form (e.g. `GOLD_PLUS`) — that tier and every tier above.
 *
 * `ALL` is the *backend* default (a blank `?eloBracket=` reads every bucket).
 * It is no longer the *page* default — see `DEFAULT_ELO_BRACKET`.
 */

/** Ranked tiers, ascending (Iron→Challenger); each apex tier is its own bucket. */
export const ELO_TIERS = [
  'IRON',
  'BRONZE',
  'SILVER',
  'GOLD',
  'PLATINUM',
  'EMERALD',
  'DIAMOND',
  'MASTER',
  'GRANDMASTER',
  'CHALLENGER',
] as const

export type EloTier = typeof ELO_TIERS[number]

export const ELO_BRACKET_ALL = 'ALL'
export const ELO_PLUS_SUFFIX = '_PLUS'

/**
 * The bracket the global champion pages open on. Master+ rather than `ALL`,
 * because the site's promise is high-elo builds and blending every tier into
 * one average is the thing it exists not to do.
 *
 * It is safe to open on: on the production aggregate, Master+ is already ~72%
 * of the games on the live patch, every champion clears the build sample floor
 * on its dominant position but two, and those two fall back to the existing
 * low-sample warning rather than an empty page.
 *
 * Deliberately *not* the default on the player-scoped champion page: there the
 * scope is one account's games, and re-slicing them by rank empties the build
 * of any truemain below Master. That page passes `ELO_BRACKET_ALL` explicitly
 * to `useChampionFilters`.
 */
export const DEFAULT_ELO_BRACKET = 'MASTER_PLUS'

/** Filter value for "this tier only". */
export function tierOnly(tier: EloTier): string {
  return tier
}

/** Filter value for "this tier and above". */
export function tierPlus(tier: EloTier): string {
  return `${tier}${ELO_PLUS_SUFFIX}`
}

/** Challenger tops the ladder, so its "+" would add nothing — hide it. */
export function hasPlus(tier: EloTier): boolean {
  return tier !== 'CHALLENGER'
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

/**
 * Resolves a raw `?elo=` param to a bracket, falling back to `fallback` when
 * it is absent *or* unrecognised. Distinct from `normalizeEloBracket`, whose
 * fallback is always `ALL`: junk must not silently widen the page to every
 * tier under a header that says otherwise.
 */
export function resolveEloBracket(
  raw: string | null | undefined,
  fallback: string = DEFAULT_ELO_BRACKET,
): string {
  if (!raw) return fallback
  const upper = raw.toUpperCase()
  return isEloBracket(upper) ? upper : fallback
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
