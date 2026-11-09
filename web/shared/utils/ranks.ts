import { formatCount } from './counts'

/**
 * Ranked tiers, ascending (Iron → Challenger), and what a rank is worth. Mirrors
 * `backend/Core/Lol/Ranking` (`EloBracket`, `RankScore`): the elo filters, the LP
 * chart and the leaderboard order all read this one list.
 */
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

const APEX_TIERS: readonly string[] = ['MASTER', 'GRANDMASTER', 'CHALLENGER'] satisfies EloTier[]

const DIVISION_ORDER: Record<string, number> = {
  IV: 0, III: 1, II: 2, I: 3,
}

export function isEloTier(value: unknown): value is EloTier {
  return typeof value === 'string' && (ELO_TIERS as readonly string[]).includes(value)
}

/** True for Master / Grandmaster / Challenger — where division is meaningless. */
export function isApexTier(tier: string | null | undefined): boolean {
  return Boolean(tier) && APEX_TIERS.includes(tier!.toUpperCase())
}

/**
 * Monotonic score for a (tier, division, leaguePoints) triple so a series of
 * rank snapshots can be plotted on one Y axis: Iron IV 0 LP is 0, Bronze IV 0 LP
 * is 400, Master 0 LP is 2800, and the apex tiers share one LP-only band above
 * it. `backend/Core/Lol/Ranking/RankScore.cs` computes the same scale to sort the
 * leaderboard; the API ships `(tier, division, leaguePoints)` without a score, so
 * the LP chart recomputes it here.
 */
export function rankScore(tier: string, division: string, leaguePoints: number): number {
  const upperTier = tier.toUpperCase()
  const tierIndex = ELO_TIERS.indexOf(upperTier as EloTier)
  if (tierIndex === -1) return 0

  if (isApexTier(upperTier)) {
    const masterFloor = ELO_TIERS.indexOf('MASTER') * 400
    return masterFloor + leaguePoints
  }

  const divisionScore = (DIVISION_ORDER[division.toUpperCase()] ?? 0) * 100
  return tierIndex * 400 + divisionScore + leaguePoints
}

/**
 * A tier as a reader writes it: `GRANDMASTER` → `Grandmaster`. Riot and the API
 * spell tiers in capitals; every label on screen is title case, as the client prints it.
 */
export function formatTierName(tier: string): string {
  return tier.charAt(0).toUpperCase() + tier.slice(1).toLowerCase()
}

/** League points as every surface writes them: `1,204 LP`. */
export function formatLeaguePoints(leaguePoints: number): string {
  return `${formatCount(leaguePoints)} LP`
}

/**
 * The one way a rank is written: `Diamond II`, `Master` (Riot returns `I` at the
 * apex, where a division means nothing), and with `lp` `Diamond II · 42 LP`.
 */
export function formatRank(
  tier: string,
  division: string | null | undefined,
  options: { lp?: number | null } = {},
): string {
  const name = formatTierName(tier)
  const rank = isApexTier(tier) || !division ? name : `${name} ${division}`
  return options.lp == null ? rank : `${rank} · ${formatLeaguePoints(options.lp)}`
}
