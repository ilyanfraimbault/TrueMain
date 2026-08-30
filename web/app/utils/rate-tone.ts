/**
 * Tailwind text colours for the three champion rates, on the `--color-data-*`
 * axis (rose gold above average → neutral below it) the palette reserves for
 * measurements — always through those tokens, never a raw `text-rosegold-*`.
 * The two share a hex today (#1096 made `--color-data-good` `rosegold-400`),
 * and the token is what keeps a future re-tint of the brand ramp from silently
 * re-tinting every measurement with it. Class strings are written out in full
 * so Tailwind's static scan generates them; a computed `text-data-${tone}`
 * would be invisible to it.
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
 * The reading `StatBlock` takes: where a value sits on the data axis, with no
 * class attached. `default` is "no such reading exists" — an unmeasured value —
 * and is distinct from `mid`, which is "measured, and it is average".
 */
export type RateBand = 'default' | 'good' | 'mid' | 'bad'

/**
 * Which side of the pack a win rate (0..1) is on. Symmetric around 50% — the
 * boundary value always belongs to the stronger band on both sides, so 51% and
 * 49% are equally emphatic.
 */
export function winRateBand(value: number | null | undefined): RateBand {
  if (value === null || value === undefined) return 'default'

  const delta = value - 0.5
  if (delta >= WIN_RATE_EDGE) return 'good'
  if (delta <= -WIN_RATE_EDGE) return 'bad'
  return 'mid'
}

/**
 * Colour for a win rate (0..1), or muted when there is nothing to colour. Same
 * bands as `winRateBand`, plus the dim step inside `WIN_RATE_DECISIVE` — a rate
 * that is ahead but not out of the pack should not shout as loudly as one that is.
 */
export function winRateTone(value: number | null | undefined): string {
  const band = winRateBand(value)
  if (band === 'default') return 'text-muted'
  if (band === 'mid') return 'text-data-mid'

  const decisive = Math.abs((value as number) - 0.5) >= WIN_RATE_DECISIVE
  if (band === 'good') return decisive ? 'text-data-good' : 'text-data-good-dim'
  return decisive ? 'text-data-bad' : 'text-data-bad-dim'
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

/**
 * Same one-sided read as `pickRateTone`, as a `StatBlock` band: notable presence
 * takes the accent end and everything else stays uncoloured. Never `bad` and
 * never `mid` — a rare pick is rare, not average and not poor.
 */
export function pickRateBand(value: number | null | undefined): RateBand {
  if (value === null || value === undefined) return 'default'
  return value >= PICK_RATE_NOTABLE ? 'good' : 'default'
}

/** Colour for a ban rate (0..1), on its own scale — see the constants above. */
export function banRateTone(value: number | null | undefined): string {
  return presenceTone(value, BAN_RATE_NOTABLE, BAN_RATE_HIGH)
}
