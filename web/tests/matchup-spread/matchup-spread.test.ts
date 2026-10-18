import { describe, expect, it } from 'vitest'
import type { ChampionMatchupEntry } from '~~/shared/types/champions'
import {
  beeswarmRows,
  buildMatchupSpread,
  matchupSpreadBand,
  matchupSpreadPosition,
} from '~~/shared/utils/matchup-spread'

/**
 * The champion page's matchup spread (#2094): opponents on the lane win rate at
 * 15 min around 50%. What it must never do: place an opponent whose lane rate is
 * unmeasured (it would read as "dead even"), or let two icons overlap.
 */

function entry(opponentChampionId: number, laneWinRate: number | null, decidedLaneGames = 20): ChampionMatchupEntry {
  return {
    opponentChampionId,
    games: 40,
    wins: 20,
    winRate: 0.5,
    playRate: 0.1,
    winRateLowerBound: 0.35,
    winRateUpperBound: 0.65,
    laneWinRate,
    decidedLaneGames,
    averageGoldDiffAt15: null,
    goldDiffLaneGames: 0,
    averageXpDiffAt15: null,
    xpDiffLaneGames: 0,
  }
}

describe('buildMatchupSpread', () => {
  it('leaves unmeasured lanes off the axis and counts them', () => {
    const spread = buildMatchupSpread([entry(1, 0.6), entry(2, null), entry(3, 0.4)])

    expect(spread.points.map(p => p.entry.opponentChampionId)).toEqual([3, 1])
    expect(spread.unmeasured).toBe(1)
  })

  it('orders hardest to easiest and names both ends', () => {
    const spread = buildMatchupSpread([entry(1, 0.55), entry(2, 0.38), entry(3, 0.62)])

    expect(spread.hardest?.entry.opponentChampionId).toBe(2)
    expect(spread.easiest?.entry.opponentChampionId).toBe(3)
  })

  it('weights the average by decided lanes, not by opponents', () => {
    const spread = buildMatchupSpread([entry(1, 0.6, 90), entry(2, 0.2, 10)])

    expect(spread.averageLaneWinRate).toBeCloseTo(0.56)
  })

  it('rounds the axis up to the widest delta, with a floor', () => {
    expect(buildMatchupSpread([entry(1, 0.51)]).halfWidth).toBeCloseTo(0.05)
    expect(buildMatchupSpread([entry(1, 0.43), entry(2, 0.58)]).halfWidth).toBeCloseTo(0.1)
  })

  it('counts each band', () => {
    const spread = buildMatchupSpread([entry(1, 0.6), entry(2, 0.51), entry(3, 0.4), entry(4, 0.45)])

    expect(spread.counts).toEqual({ favourable: 1, even: 1, hard: 2 })
  })
})

describe('matchupSpreadBand', () => {
  it('keeps the band edge inside even', () => {
    expect(matchupSpreadBand(0.025)).toBe('even')
    expect(matchupSpreadBand(-0.025)).toBe('even')
    expect(matchupSpreadBand(0.026)).toBe('favourable')
  })
})

describe('matchupSpreadPosition', () => {
  it('maps 50% to the centre and clamps the ends', () => {
    expect(matchupSpreadPosition(0, 0.1)).toBe(0.5)
    expect(matchupSpreadPosition(-0.1, 0.1)).toBe(0)
    expect(matchupSpreadPosition(0.3, 0.1)).toBe(1)
  })
})

describe('beeswarmRows', () => {
  it('keeps distant marks on the centre row', () => {
    expect(beeswarmRows([0, 0.5, 1], 0.1)).toEqual([0, 0, 0])
  })

  it('stacks close marks outward, alternating sides', () => {
    expect(beeswarmRows([0.5, 0.51, 0.52, 0.53], 0.1)).toEqual([0, -1, 1, -2])
  })

  it('never puts two marks closer than the gap on one row', () => {
    const xs = Array.from({ length: 40 }, (_, i) => (i * 37 % 100) / 100)
    const rows = beeswarmRows(xs, 0.08)
    for (let i = 0; i < xs.length; i++) {
      for (let j = i + 1; j < xs.length; j++) {
        if (rows[i] === rows[j]) expect(Math.abs(xs[i]! - xs[j]!)).toBeGreaterThanOrEqual(0.08 - 1e-9)
      }
    }
  })
})
