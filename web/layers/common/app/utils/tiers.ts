import { ELO_TIERS } from '#shared/utils/ranks'
import { THEME_HEX } from '#shared/utils/theme-tokens'

// Riot's rank colours (`--color-rank-*` in theme.css, which says why they are
// off-palette), as the profile card, the leaderboard and the LP chart use them.

/** Tailwind classes, spelled out so Tailwind finds them in the source. */
export const TIER_COLORS: Record<string, string> = {
  IRON: 'text-rank-iron',
  BRONZE: 'text-rank-bronze',
  SILVER: 'text-rank-silver',
  GOLD: 'text-rank-gold',
  PLATINUM: 'text-rank-platinum',
  EMERALD: 'text-rank-emerald',
  DIAMOND: 'text-rank-diamond',
  MASTER: 'text-rank-master',
  GRANDMASTER: 'text-rank-grandmaster',
  CHALLENGER: 'text-rank-challenger',
}

/** The same colours as hex, for chart libraries and the share card. */
export const TIER_HEX = Object.fromEntries(
  ELO_TIERS.map(tier => [tier, THEME_HEX[`rank-${tier.toLowerCase()}` as keyof typeof THEME_HEX]]),
) as Record<string, string>

/** Returns a hex color for the tier, or the neutral ink-500 — never a guessed rank. */
export function tierHex(tier: string | null | undefined): string {
  return (tier && TIER_HEX[tier.toUpperCase()]) || THEME_HEX['ink-500']
}

/** Returns a Tailwind text-colour class for the tier, or a muted default. */
export function tierColor(tier: string | null | undefined): string {
  if (!tier) return 'text-muted'
  return TIER_COLORS[tier.toUpperCase()] ?? 'text-default'
}
