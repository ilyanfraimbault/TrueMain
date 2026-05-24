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
