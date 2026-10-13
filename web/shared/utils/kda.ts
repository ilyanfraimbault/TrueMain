/**
 * The KDA ratio, the one way every surface computes and writes it (#2031).
 *
 * Two readings, one rule each:
 * - **A game's line** (`formatKda`): a deathless game is `Perfect`, not a big
 *   number — that is what the player did, and what the League client says.
 * - **A ratio over a sample** (`kdaRatio`, `formatKdaRatio`): deaths are floored
 *   at one, so a deathless stretch is a large finite number that still sorts,
 *   averages and compares, instead of an `Infinity` that breaks all three.
 *
 * Two decimals everywhere ({@link KDA_DIGITS}), the precision League players
 * read a KDA at: `3.67`, not `3.7` on one surface and `3.67` on the next.
 */

export interface KdaLine {
  kills: number
  deaths: number
  assists: number
}

/** The decimals a KDA ratio is written with, on every surface. */
export const KDA_DIGITS = 2

/** `(kills + assists) / deaths`, deaths floored at one. */
export function kdaRatio(line: KdaLine): number {
  return (line.kills + line.assists) / Math.max(line.deaths, 1)
}

/** A ratio already computed (an aggregate, an API field): `3.67`. */
export function formatKdaRatio(ratio: number): string {
  return ratio.toFixed(KDA_DIGITS)
}

/** One game's line as its label: `3.67 KDA`, or `Perfect` when the player never died. */
export function formatKda(line: KdaLine): string {
  return line.deaths === 0 ? 'Perfect' : `${formatKdaRatio(kdaRatio(line))} KDA`
}
