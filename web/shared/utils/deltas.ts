/**
 * Signed differences, the one way every surface writes them (#2031).
 *
 * - The sign is always written, as `+` or the typographic minus `−` (U+2212, the
 *   width of the plus, so a column of deltas stays aligned); a difference that
 *   rounds to zero carries none, because `+0.0` claims a direction it does not have.
 * - A difference between two rates (win rate, pick rate, a win probability) is in
 *   **percentage points**, written `pts`: a win rate going from 50% to 53% moved
 *   `+3.0 pts`, not `+6%`.
 * - Grouping is pinned to `en-US`, like `formatCount`, so the server and the
 *   browser render the same string.
 */

const MINUS = '−'

function magnitude(value: number, digits: number): string {
  return Math.abs(value).toLocaleString('en-US', { minimumFractionDigits: digits, maximumFractionDigits: digits })
}

/** A signed number at a fixed precision: `+312`, `−1,204`, `+0.25`, `0`. */
export function formatSigned(value: number, digits = 0): string {
  const text = magnitude(value, digits)
  if (Number(text.replaceAll(',', '')) === 0) return text
  return `${value > 0 ? '+' : MINUS}${text}`
}

export interface PointDeltaOptions {
  /** Decimals of the points figure. One by default: `+3.1 pts`. */
  digits?: number
  /** Append the `pts` unit. Off where the unit is drawn apart or said once for a whole list. */
  unit?: boolean
  /** Write the sign. Off where an arrow already gives the direction. */
  signed?: boolean
}

/**
 * The difference between two 0..1 rates, in percentage points: `0.031` → `+3.1 pts`.
 */
export function formatPointDelta(delta: number, options: PointDeltaOptions = {}): string {
  const { digits = 1, unit = true, signed = true } = options
  const points = delta * 100
  const text = signed ? formatSigned(points, digits) : magnitude(points, digits)
  return unit ? `${text} pts` : text
}
