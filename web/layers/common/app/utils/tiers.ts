// Tier visual + label helpers shared across the profile card and the
// leaderboard row.
//
// This is the one map in the app that is deliberately off-palette: these are
// *Riot's* rank colours, not TrueMain's. A player reads Iron/Bronze/Gold from
// the colour before the word, so reproducing the in-client cues beats making
// them agree with the brand. They never collide with the data axis in practice
// because a rank names a bracket rather than scoring anything.

export const TIER_COLORS: Record<string, string> = {
  IRON: 'text-stone-400',
  BRONZE: 'text-amber-700',
  SILVER: 'text-slate-300',
  GOLD: 'text-amber-400',
  PLATINUM: 'text-teal-300',
  EMERALD: 'text-emerald-300',
  DIAMOND: 'text-blue-400',
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
  DIAMOND: '#60a5fa',     // blue-400
  MASTER: '#f0abfc',      // fuchsia-300
  GRANDMASTER: '#fca5a5', // red-300
  CHALLENGER: '#a5f3fc',  // cyan-200
}

/** Returns a hex color for the tier, or a muted default. */
export function tierHex(tier: string | null | undefined): string {
  if (!tier) return '#6a6a74' // ink-500 — the neutral fallback, never a guessed rank
  return TIER_HEX[tier.toUpperCase()] ?? '#6a6a74'
}

/** Returns a Tailwind text-colour class for the tier, or a muted default. */
export function tierColor(tier: string | null | undefined): string {
  if (!tier) return 'text-muted'
  return TIER_COLORS[tier.toUpperCase()] ?? 'text-default'
}
