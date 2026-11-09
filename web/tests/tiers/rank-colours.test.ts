import { describe, expect, it } from 'vitest'
import { TIER_COLORS, tierColor, tierHex } from '#common/utils/tiers'
import { ELO_TIERS } from '~~/shared/utils/ranks'
import { THEME_HEX } from '~~/shared/utils/theme-tokens'

describe('rank colours', () => {
  it('give every ranked tier a theme token, as a class and as hex', () => {
    for (const tier of ELO_TIERS) {
      expect(TIER_COLORS[tier]).toBe(`text-rank-${tier.toLowerCase()}`)
      expect(tierHex(tier)).toMatch(/^#[0-9a-f]{6}$/)
    }
  })

  it('read the tier however the API cases it', () => {
    expect(tierHex('diamond')).toBe(THEME_HEX['rank-diamond'])
    expect(tierColor('Diamond')).toBe('text-rank-diamond')
  })

  it('never guess a rank: neutral for a missing or unknown tier', () => {
    expect(tierHex(null)).toBe(THEME_HEX['ink-500'])
    expect(tierHex('WOOD')).toBe(THEME_HEX['ink-500'])
    expect(tierColor(undefined)).toBe('text-muted')
    expect(tierColor('WOOD')).toBe('text-default')
  })
})
