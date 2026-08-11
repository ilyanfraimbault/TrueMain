/**
 * Tailwind text colours for the three champion rates, on the `--color-data-*`
 * axis (rose gold above average → neutral below it) the palette reserves for
 * measurements
 * — never the rose-gold accent, which means "interactive" (see the #1060
 * decision). Class strings are written out in full so Tailwind's static scan
 * generates them; a computed `text-data-${tone}` would be invisible to it.
 *
 * Only the win rate rides the good↔bad axis. Pick and ban rate are *presence*,
 * not performance: a niche champion is not "bad" at being picked, so those two
 * use a one-sided ramp that fades to muted — and they
 * do not share a scale with each other either (see the constants below).
 */

/** Win rates inside ±this of 50% are noise, not an edge. */
export const WIN_RATE_EDGE = 0.01

/** Past this gap from 50%, the champion is not just ahead but out of the pack. */
export const WIN_RATE_DECISIVE = 0.03

/**
 * Colour for a win rate (0..1), or muted when there is nothing to colour.
 * Symmetric around 50% — the boundary value always belongs to the stronger band
 * on both sides, so 51% and 49% are equally emphatic.
 */
export function winRateTone(value: number | null | undefined): string {
  if (value === null || value === undefined) return 'text-muted'

  const delta = value - 0.5
  if (delta >= WIN_RATE_DECISIVE) return 'text-data-good'
  if (delta >= WIN_RATE_EDGE) return 'text-data-good-dim'
  if (delta <= -WIN_RATE_DECISIVE) return 'text-data-bad'
  if (delta <= -WIN_RATE_EDGE) return 'text-data-bad-dim'
  return 'text-data-mid'
}

/**
 * Pick and ban rate share a shape — "how present is this champion" — but not a
 * scale, so they get their own bands. Both are calibrated against the live
 * tier list on patch 16.15 (561 rows) rather than guessed, because a threshold
 * no row ever crosses is a colour that never appears.
 *
 * Pick rate's denominator is mains' games *at that lane*, split across ~110
 * champion rows per lane: the median row sits at 0.3% and the busiest champion
 * on the patch reaches 7.6%. 2% is roughly the top sixth of the list, 4% its
 * top thirtieth.
 */
export const PICK_RATE_HIGH = 0.04
export const PICK_RATE_NOTABLE = 0.02

/**
 * Ban rate counts every observed match, so it runs an order of magnitude
 * higher — median 2.6%, and the most-hated champion of the patch is banned in
 * 59.5% of games. 5% is the third of the list anyone bothers to ban; 15% is a
 * champion the lobby regularly spends a ban slot on.
 */
export const BAN_RATE_HIGH = 0.15
export const BAN_RATE_NOTABLE = 0.05

function presenceTone(value: number | null | undefined, notable: number, high: number): string {
  if (value === null || value === undefined) return 'text-muted'
  if (value >= high) return 'text-data-good'
  if (value >= notable) return 'text-data-good-dim'
  return 'text-muted'
}

/**
 * Colour for a pick rate (0..1). One-sided on purpose: high presence gets the
 * accent end, everything else stays muted rather than being accused of being bad.
 */
export function pickRateTone(value: number | null | undefined): string {
  return presenceTone(value, PICK_RATE_NOTABLE, PICK_RATE_HIGH)
}

/** Colour for a ban rate (0..1), on its own scale — see the constants above. */
export function banRateTone(value: number | null | undefined): string {
  return presenceTone(value, BAN_RATE_NOTABLE, BAN_RATE_HIGH)
}
