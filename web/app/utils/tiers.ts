import { ELO_TIERS } from '~/utils/elo-brackets'

// Tier visual + label helpers shared across the profile card and the
// leaderboard row. The colour map intentionally keeps the warm cues for
// Iron→Gold (players read those ranks from the colour first) and uses the
// project's emerald-leaning palette elsewhere.

export const TIER_COLORS: Record<string, string> = {
  IRON: 'text-stone-400',
  BRONZE: 'text-amber-700',
  SILVER: 'text-slate-300',
  GOLD: 'text-amber-400',
  PLATINUM: 'text-teal-300',
  EMERALD: 'text-emerald-300',
  DIAMOND: 'text-sky-300',
  MASTER: 'text-fuchsia-300',
  GRANDMASTER: 'text-red-300',
  CHALLENGER: 'text-cyan-200',
}

// Hex equivalents of TIER_COLORS — needed by chart libraries that accept a
// raw color string instead of a Tailwind class. Kept in lockstep with the
// shade chosen above (mostly -300) so the chart fill matches how the same
// tier reads elsewhere in the UI.
export const TIER_HEX: Record<string, string> = {
  IRON: '#a8a29e',        // stone-400
  BRONZE: '#b45309',      // amber-700
  SILVER: '#cbd5e1',      // slate-300
  GOLD: '#fbbf24',        // amber-400
  PLATINUM: '#5eead4',    // teal-300
  EMERALD: '#6ee7b7',     // emerald-300
  DIAMOND: '#7dd3fc',     // sky-300
  MASTER: '#f0abfc',      // fuchsia-300
  GRANDMASTER: '#fca5a5', // red-300
  CHALLENGER: '#a5f3fc',  // cyan-200
}

// The ranked ladder order is owned by elo-brackets.ts (mirroring the backend
// EloBracket enum) — alias it so the two lists can't drift.
const TIER_ORDER = ELO_TIERS

const DIVISION_ORDER: Record<string, number> = {
  IV: 0, III: 1, II: 2, I: 3,
}

/** Returns a hex color for the tier, or a muted default. */
export function tierHex(tier: string | null | undefined): string {
  if (!tier) return '#71717a' // zinc-500
  return TIER_HEX[tier.toUpperCase()] ?? '#71717a'
}

/**
 * Monotonic score for a (tier, division, leaguePoints) triple so a series
 * of rank snapshots can be plotted on a single Y axis. Iron IV 0 LP is 0,
 * Bronze IV 0 LP is 400, Emerald IV 0 LP is 2000, Master 0 LP is 2800,
 * and Master / GrandMaster / Challenger share a continuous LP-only band
 * above 2800 since Riot dropped division-based promo for the apex tiers.
 */
export function rankScore(tier: string, division: string, leaguePoints: number): number {
  const upperTier = tier.toUpperCase()
  const tierIndex = TIER_ORDER.indexOf(upperTier as typeof TIER_ORDER[number])
  if (tierIndex === -1) return 0

  // Master+ collapse to one continuous band rooted at the Master floor.
  if (isApexTier(upperTier)) {
    const masterFloor = TIER_ORDER.indexOf('MASTER') * 400
    return masterFloor + leaguePoints
  }

  const divisionScore = (DIVISION_ORDER[division.toUpperCase()] ?? 0) * 100
  return tierIndex * 400 + divisionScore + leaguePoints
}

/** The ordered list of tier names — exposed so callers can iterate Y-axis bands. */
export const TIER_NAMES: readonly string[] = TIER_ORDER

/** Returns a Tailwind text-colour class for the tier, or a muted default. */
export function tierColor(tier: string | null | undefined): string {
  if (!tier) return 'text-muted'
  return TIER_COLORS[tier.toUpperCase()] ?? 'text-default'
}

/** True for Master / Grandmaster / Challenger — where division is meaningless. */
export function isApexTier(tier: string | null | undefined): boolean {
  if (!tier) return false
  const upper = tier.toUpperCase()
  return upper === 'MASTER' || upper === 'GRANDMASTER' || upper === 'CHALLENGER'
}

/**
 * Formats a tier+division pair the way the UI displays it. Master+ rows
 * drop the division because Riot returns "I" for them but the division has
 * no meaning at the apex.
 */
export function formatTier(tier: string, division: string): string {
  if (isApexTier(tier)) return tier
  return `${tier} ${division}`
}
