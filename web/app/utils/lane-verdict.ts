/**
 * Bands the average gold gap at 15 minutes into a one-phrase verdict (#976).
 *
 * The backend returns the gap and its sample and stops there — where "even" ends
 * and "good" begins is a product judgement, not a measurement, and keeping it here
 * means moving it costs a frontend edit rather than a re-fold of every stored row.
 */

/** Below this gap either way, the lane is even — noise, not an edge. */
export const LANE_EVEN_GOLD = 150

/** Past this gap, the lane is not just won but decided. */
export const LANE_DOMINANT_GOLD = 300

/**
 * Lanes the average must rest on before it earns a label. Matches the backend's
 * matchup games floor: below it a single stomp moves the average by hundreds of
 * gold, and a band is a stronger claim than the number it came from — so the
 * figure still shows, the verdict does not.
 */
export const LANE_VERDICT_MIN_GAMES = 10

export interface LaneVerdict {
  /** e.g. "Very good lane" — already carries the noun. */
  label: string
  /** Nuxt UI color token for the badge. */
  color: 'success' | 'warning' | 'error' | 'neutral'
  /**
   * Ringed (`subtle`) for the two decided bands, flat `soft` for the rest, so the
   * strength of the call reads before the words do — without a solid block of colour,
   * which the page reserves for its own accent.
   */
  variant: 'subtle' | 'soft'
}

/**
 * The verdict for an average gold gap, or `null` when there is nothing to call —
 * no measured gap, or too few lanes behind it.
 *
 * `noun` exists for the jungle, which has no lane (#939): the caller passes
 * "matchup" there and "lane" everywhere else.
 */
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

/** Green ahead, red behind, muted inside the even band — same read as a win rate. */
export function goldDiffTone(value: number): string {
  if (value >= LANE_EVEN_GOLD) return 'text-emerald-400'
  if (value <= -LANE_EVEN_GOLD) return 'text-red-400'
  return 'text-muted'
}
