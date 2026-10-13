import { describe, expect, it } from 'vitest'
import { formatLeaguePoints, formatRank, formatTierName } from '#common/utils/tiers'

/**
 * Every rank label on the site and in the app goes through these three, so a
 * player's rank reads the same on every surface (#2025): title case, no
 * division at the apex, LP thousands-separated after a middle dot.
 */
describe('formatTierName', () => {
  it('writes a tier in title case', () => {
    expect(formatTierName('GRANDMASTER')).toBe('Grandmaster')
    expect(formatTierName('diamond')).toBe('Diamond')
  })
})

describe('formatLeaguePoints', () => {
  it('separates thousands', () => {
    expect(formatLeaguePoints(42)).toBe('42 LP')
    expect(formatLeaguePoints(1204)).toBe('1,204 LP')
  })
})

describe('formatRank', () => {
  it('pairs the tier with its division below the apex', () => {
    expect(formatRank('DIAMOND', 'II')).toBe('Diamond II')
  })

  it('drops the division at the apex', () => {
    expect(formatRank('MASTER', 'I')).toBe('Master')
    expect(formatRank('CHALLENGER', 'I', { lp: 1204 })).toBe('Challenger · 1,204 LP')
  })

  it('appends the league points when asked', () => {
    expect(formatRank('GOLD', 'IV', { lp: 0 })).toBe('Gold IV · 0 LP')
    expect(formatRank('GOLD', 'IV', { lp: null })).toBe('Gold IV')
  })
})
