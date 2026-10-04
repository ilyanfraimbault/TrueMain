import { describe, expect, it } from 'vitest'
import type { LaningForm } from '~/types/loading'
import { ROLE_WEIGHTS, laneEdge, laneForm, matchupChance } from '~/utils/lane-edge'

function laning(games: number, wins: number, gold: number | null = null, cs: number | null = null, xp: number | null = null): LaningForm {
  return { games, wins, measured: gold === null ? 0 : games, goldDiff15: gold, csDiff15: cs, xpDiff15: xp }
}

describe('ROLE_WEIGHTS', () => {
  it('sums to one in every role', () => {
    for (const weights of Object.values(ROLE_WEIGHTS)) {
      expect(Object.values(weights).reduce((sum, weight) => sum + weight, 0)).toBeCloseTo(1)
    }
  })
})

describe('laneForm', () => {
  it('is even with nothing read, a first time or an unknown role', () => {
    expect(laneForm(null, 'TOP')).toBe(0)
    expect(laneForm(laning(0, 0), 'TOP')).toBe(0)
    expect(laneForm(laning(10, 10, 1000, 15, 800), '')).toBe(0)
  })

  it('rewards winning and leading, and punishes losing and trailing', () => {
    expect(laneForm(laning(10, 8, 800, 12, 600), 'MIDDLE')).toBeGreaterThan(0.4)
    expect(laneForm(laning(10, 2, -800, -12, -600), 'MIDDLE')).toBeLessThan(-0.4)
  })

  it('pulls a single game towards even', () => {
    expect(laneForm(laning(1, 1), 'TOP')).toBeLessThan(laneForm(laning(10, 10), 'TOP'))
  })

  it('ignores a support\'s farm and weighs mostly their win rate', () => {
    expect(laneForm(laning(10, 5, 0, 15, 0), 'UTILITY')).toBe(0)
    expect(laneForm(laning(10, 5, 0, 15, 0), 'BOTTOM')).toBeGreaterThan(0)
    expect(Math.abs(laneForm(laning(10, 9, 0, 0, 0), 'UTILITY'))).toBeGreaterThan(Math.abs(laneForm(laning(10, 5, 1000, 0, 0), 'UTILITY')))
  })

  it('caps a huge lead at its full weight', () => {
    expect(laneForm(laning(10, 5, 9000, 0, 0), 'JUNGLE')).toBeCloseTo(laneForm(laning(10, 5, 1000 * 12 / 10, 0, 0), 'JUNGLE'))
  })
})

describe('matchupChance', () => {
  it('starts even with no data and pulls a thin sample towards it', () => {
    expect(matchupChance(null)).toBe(0.5)
    expect(matchupChance({ games: 2, wins: 2 })).toBeCloseTo(0.53, 2)
    expect(matchupChance({ games: 3000, wins: 1650 })).toBeCloseTo(0.55, 2)
  })
})

describe('laneEdge', () => {
  it('points at the enemy when our player keeps losing on the champion and theirs keeps winning', () => {
    const edge = laneEdge('JUNGLE', { games: 2000, wins: 1000 }, laning(10, 2, -700, -10, -500), laning(10, 8, 600, 8, 400))
    expect(edge.side).toBe('enemy')
    expect(edge.percent).toBeGreaterThan(60)
    expect(edge.chance).toBeCloseTo(1 - edge.percent / 100, 1)
  })

  it('follows the matchup alone when no form was read', () => {
    const edge = laneEdge('TOP', { games: 5000, wins: 2850 }, null, null)
    expect(edge.side).toBe('ally')
    expect(edge.percent).toBe(57)
    expect(edge.matchupGames).toBe(5000)
  })

  it('calls a near-even lane even', () => {
    expect(laneEdge('MIDDLE', null, laning(10, 5), laning(10, 5)).side).toBe('even')
  })

  it('is symmetric: swapping the players mirrors the chance', () => {
    const a = laning(10, 7, 300, 5, 200)
    const b = laning(6, 2, -200, -3, -100)
    expect(laneEdge('BOTTOM', null, a, b).chance).toBeCloseTo(1 - laneEdge('BOTTOM', null, b, a).chance)
  })
})
