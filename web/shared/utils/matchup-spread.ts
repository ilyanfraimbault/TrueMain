import type { ChampionMatchupEntry } from '../types/champions'

/**
 * The champion page's matchup spread (#2094): every lane opponent placed on one
 * axis by the **lane** win rate at 15 minutes — the share of decided lanes the
 * champion came out of ahead in gold — never by the game win rate. A lane is
 * judged against 50% rather than against the champion's own average: "am I ahead
 * at 15 against this pick" has an absolute answer, and the average would move the
 * centre line every time the opponent pool shifts.
 */

/** Lane win rates inside ±this of 50% read as even. */
export const MATCHUP_SPREAD_EVEN_BAND = 0.025

/** Narrowest half-width of the axis, so a flat field is not stretched edge to edge. */
const MIN_HALF_WIDTH = 0.05

/** The axis half-width is rounded up to a multiple of this. */
const HALF_WIDTH_STEP = 0.05

export type MatchupSpreadBand = 'favourable' | 'even' | 'hard'

export interface MatchupSpreadPoint {
  entry: ChampionMatchupEntry
  laneWinRate: number
  /** `laneWinRate - 0.5`, signed from the champion's side. */
  delta: number
  band: MatchupSpreadBand
}

export interface MatchupSpread {
  /** Opponents with a lane win rate, ordered hardest → easiest. */
  points: MatchupSpreadPoint[]
  /** Opponents on the list whose decided lanes are under the floor (null rate). */
  unmeasured: number
  /** Half-width of the symmetric axis around 50%, in rate units. */
  halfWidth: number
  /** Decided-lane-weighted lane win rate over the measured opponents. */
  averageLaneWinRate: number | null
  counts: Record<MatchupSpreadBand, number>
  hardest: MatchupSpreadPoint | null
  easiest: MatchupSpreadPoint | null
}

export function matchupSpreadBand(delta: number): MatchupSpreadBand {
  if (delta > MATCHUP_SPREAD_EVEN_BAND) return 'favourable'
  if (delta < -MATCHUP_SPREAD_EVEN_BAND) return 'hard'
  return 'even'
}

export function buildMatchupSpread(entries: readonly ChampionMatchupEntry[]): MatchupSpread {
  const points: MatchupSpreadPoint[] = []
  let unmeasured = 0
  let wonLanes = 0
  let decidedLanes = 0

  for (const entry of entries) {
    if (entry.laneWinRate === null) {
      unmeasured++
      continue
    }
    const delta = entry.laneWinRate - 0.5
    points.push({ entry, laneWinRate: entry.laneWinRate, delta, band: matchupSpreadBand(delta) })
    wonLanes += entry.laneWinRate * entry.decidedLaneGames
    decidedLanes += entry.decidedLaneGames
  }

  // Opponent id breaks ties so the same data always lays out the same way.
  points.sort((a, b) => a.delta - b.delta || a.entry.opponentChampionId - b.entry.opponentChampionId)

  const widest = points.reduce((max, p) => Math.max(max, Math.abs(p.delta)), 0)
  const halfWidth = Math.max(MIN_HALF_WIDTH, Math.ceil(widest / HALF_WIDTH_STEP - 1e-9) * HALF_WIDTH_STEP)

  const counts: Record<MatchupSpreadBand, number> = { favourable: 0, even: 0, hard: 0 }
  for (const p of points) counts[p.band]++

  return {
    points,
    unmeasured,
    halfWidth,
    averageLaneWinRate: decidedLanes > 0 ? wonLanes / decidedLanes : null,
    counts,
    hardest: points[0] ?? null,
    easiest: points.at(-1) ?? null,
  }
}

/** Where a delta sits on the axis, 0 (left edge) → 1 (right edge). */
export function matchupSpreadPosition(delta: number, halfWidth: number): number {
  return Math.min(1, Math.max(0, 0.5 + delta / (2 * halfWidth)))
}

/** The `step`-th row tried, outward from the centre: 0, −1, 1, −2, 2… */
function beeswarmRow(step: number): number {
  if (step === 0) return 0
  return step % 2 === 1 ? -(step + 1) / 2 : step / 2
}

/**
 * Beeswarm rows: for each x (0..1, in the order given), the signed row it is
 * drawn on so no two marks closer than `minGap` share a row. Rows fill outward
 * from the centre line — 0, −1, 1, −2, 2… — so a crowded band grows both ways
 * instead of stacking up from the floor.
 */
export function beeswarmRows(xs: readonly number[], minGap: number): number[] {
  const occupied = new Map<number, number[]>()
  return xs.map((x) => {
    for (let step = 0; ; step++) {
      const row = beeswarmRow(step)
      const taken = occupied.get(row) ?? []
      if (taken.every(other => Math.abs(other - x) >= minGap - 1e-9)) {
        taken.push(x)
        occupied.set(row, taken)
        return row
      }
    }
  })
}
