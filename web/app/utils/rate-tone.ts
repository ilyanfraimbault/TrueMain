/**
 * Tailwind text colours for the three champion rates, on the `--color-data-*`
 * axis (teal good → neutral → amber bad) the palette reserves for measurements
 * — never the rose-gold accent, which means "interactive" (see the #1060
 * decision). Class strings are written out in full so Tailwind's static scan
 * generates them; a computed `text-data-${tone}` would be invisible to it.
 *
 * Only the win rate rides the good↔bad axis. Pick and ban rate are *presence*,
 * not performance: a niche champion is not "bad" at being picked, so those two
 * use a one-sided ramp that fades to muted instead of turning amber.
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
 * Above this share, the champion is a staple of the lane (or a ban the lobby
 * spends a slot on). Pick rate's denominator is mains' games *at that lane*, so
 * a lane of ~20 viable champions puts the average around 5% — the two bands are
 * "roughly one of the standard picks" and "clearly more than that".
 */
export const PRESENCE_HIGH = 0.1

/** Above this share, the champion is a regular sight rather than a pocket pick. */
export const PRESENCE_NOTABLE = 0.05

/**
 * Colour for a pick or ban rate (0..1). One-sided on purpose: high presence
 * gets the teal end, everything else stays muted rather than being accused of
 * being bad.
 */
export function presenceTone(value: number | null | undefined): string {
  if (value === null || value === undefined) return 'text-muted'
  if (value >= PRESENCE_HIGH) return 'text-data-good'
  if (value >= PRESENCE_NOTABLE) return 'text-data-good-dim'
  return 'text-muted'
}
