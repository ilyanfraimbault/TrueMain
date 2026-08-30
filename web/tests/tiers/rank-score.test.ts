import { describe, expect, it } from 'vitest'
import { rankScore } from '../../app/utils/tiers'

/**
 * `rankScore` mirrors `backend/Core/Lol/Ranking/RankScore.cs`: the backend sorts
 * the truemains leaderboard with it, the front plots the profile LP curve with
 * it. Nothing at runtime compares the two, so these anchors are what catches a
 * drift — a new tier, a changed division weight or a moved apex floor breaks a
 * named expectation here instead of silently skewing the chart's Y axis while
 * the ladder keeps its old order.
 */
describe('rankScore', () => {
  it('anchors the bottom of the ladder at zero', () => {
    expect(rankScore('IRON', 'IV', 0)).toBe(0)
  })

  it('spaces tiers 400 apart and divisions 100 apart', () => {
    expect(rankScore('BRONZE', 'IV', 0)).toBe(400)
    expect(rankScore('EMERALD', 'IV', 0)).toBe(2000)
    expect(rankScore('EMERALD', 'I', 0)).toBe(2300)
    expect(rankScore('GOLD', 'II', 42)).toBe(1200 + 200 + 42)
  })

  it('never lets LP bridge the gap to the next division', () => {
    expect(rankScore('SILVER', 'IV', 99)).toBeLessThan(rankScore('SILVER', 'III', 0))
    expect(rankScore('SILVER', 'I', 99)).toBeLessThan(rankScore('GOLD', 'IV', 0))
  })

  it('collapses the apex tiers onto one continuous LP-only band at 2800', () => {
    expect(rankScore('MASTER', 'I', 0)).toBe(2800)
    expect(rankScore('GRANDMASTER', 'I', 0)).toBe(2800)
    expect(rankScore('CHALLENGER', 'I', 0)).toBe(2800)
    // Above the floor only raw LP separates them — a Master on 900 LP outranks
    // a Challenger on 300, the same tie-break the backend applies.
    expect(rankScore('MASTER', 'I', 900)).toBeGreaterThan(rankScore('CHALLENGER', 'I', 300))
  })

  it('is case-insensitive and falls back to zero on an unknown tier', () => {
    expect(rankScore('emerald', 'iv', 0)).toBe(2000)
    expect(rankScore('UNRANKED', 'IV', 50)).toBe(0)
  })
})
