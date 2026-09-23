/**
 * Copy of `web/app/utils/lane-verdict.ts` (verdict and formatters only) until
 * the shared layer (#1687) lets the desktop import the site's own. Keep the
 * bands identical: "a hard lane" must mean the same thing in both places.
 *
 * Bands the average gold gap at 15 minutes into a one-phrase verdict (#976).
 */

/** Below this gap either way, the lane is even — noise, not an edge. */
export const LANE_EVEN_GOLD = 150

/** Past this gap, the lane is not just won but decided. */
export const LANE_DOMINANT_GOLD = 300

/** Lanes the average must rest on before it earns a label. */
export const LANE_VERDICT_MIN_GAMES = 10

export interface LaneVerdict {
  label: string
  color: 'success' | 'warning' | 'error' | 'neutral'
  variant: 'subtle' | 'soft'
}

export function laneVerdict(
  averageGoldDiffAt15: number | null,
  sampleLanes: number,
  noun: 'lane' | 'matchup' = 'lane',
): LaneVerdict | null {
  if (averageGoldDiffAt15 === null || sampleLanes < LANE_VERDICT_MIN_GAMES) return null

  if (averageGoldDiffAt15 >= LANE_DOMINANT_GOLD) {
    return { label: `Very good ${noun}`, color: 'success', variant: 'subtle' }
  }
  if (averageGoldDiffAt15 >= LANE_EVEN_GOLD) {
    return { label: `Good ${noun}`, color: 'success', variant: 'soft' }
  }
  if (averageGoldDiffAt15 <= -LANE_DOMINANT_GOLD) {
    return { label: `Hard ${noun}`, color: 'error', variant: 'subtle' }
  }
  if (averageGoldDiffAt15 <= -LANE_EVEN_GOLD) {
    return { label: `Bad ${noun}`, color: 'warning', variant: 'soft' }
  }
  return { label: `Even ${noun}`, color: 'neutral', variant: 'soft' }
}

/** `+312` / `−184` / `0` — always signed, so the side it favours is never ambiguous. */
export function formatGoldDiff(value: number): string {
  const rounded = Math.round(value)
  const sign = rounded > 0 ? '+' : rounded < 0 ? '−' : ''
  return `${sign}${Math.abs(rounded).toLocaleString('en-US')}`
}

/** Win rates inside ±this of 50% are noise, not an edge. */
const WIN_RATE_EDGE = 0.01
/** Past this gap from 50%, the rate is not just ahead but out of the pack. */
const WIN_RATE_DECISIVE = 0.03

/**
 * Colour for a win rate — the site's `winRateTone` (`web/app/utils/rate-tone.ts`):
 * rose gold above average, fading to ink below it, on the `--color-data-*` axis.
 */
export function winRateTone(value: number | null | undefined): string {
  if (value === null || value === undefined) return 'text-muted'
  const delta = value - 0.5
  if (Math.abs(delta) < WIN_RATE_EDGE) return 'text-data-mid'
  const decisive = Math.abs(delta) >= WIN_RATE_DECISIVE
  if (delta > 0) return decisive ? 'text-data-good' : 'text-data-good-dim'
  return decisive ? 'text-data-bad' : 'text-data-bad-dim'
}
