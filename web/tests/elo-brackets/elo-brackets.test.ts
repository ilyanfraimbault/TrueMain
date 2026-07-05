import { describe, expect, it } from 'vitest'
import {
  ELO_TIERS,
  ELO_BRACKET_ALL,
  tierOnly,
  tierPlus,
  hasPlus,
  isEloTier,
  isEloBracket,
  normalizeEloBracket,
  eloBracketLabel,
} from '~~/app/utils/elo-brackets'

describe('elo-brackets', () => {
  describe('ELO_TIERS', () => {
    it('is the eight ranked tiers, ascending, ending at Master', () => {
      expect([...ELO_TIERS]).toEqual([
        'IRON', 'BRONZE', 'SILVER', 'GOLD', 'PLATINUM', 'EMERALD', 'DIAMOND', 'MASTER',
      ])
    })
  })

  describe('tierOnly / tierPlus / hasPlus', () => {
    it('builds the bare and "and above" filter values', () => {
      expect(tierOnly('GOLD')).toBe('GOLD')
      expect(tierPlus('GOLD')).toBe('GOLD_PLUS')
    })

    it('offers a "+" for every tier except the top one (Master)', () => {
      expect(hasPlus('IRON')).toBe(true)
      expect(hasPlus('DIAMOND')).toBe(true)
      expect(hasPlus('MASTER')).toBe(false)
    })
  })

  describe('isEloTier', () => {
    it('accepts the canonical upper-case tiers only', () => {
      expect(isEloTier('GOLD')).toBe(true)
      expect(isEloTier('MASTER')).toBe(true)
    })

    it.each(['gold', 'GRANDMASTER', 'CHALLENGER', 'UNRANKED', 'ALL', '', null, 42])(
      'rejects %p',
      (value) => {
        expect(isEloTier(value)).toBe(false)
      },
    )
  })

  describe('isEloBracket', () => {
    it.each(['ALL', 'GOLD', 'GOLD_PLUS', 'IRON', 'MASTER'])('accepts %p', (value) => {
      expect(isEloBracket(value)).toBe(true)
    })

    it.each(['gold', 'GOLD_MINUS', 'UNRANKED', 'UNRANKED_PLUS', 'garbage', '', 7, null])(
      'rejects %p',
      (value) => {
        expect(isEloBracket(value)).toBe(false)
      },
    )
  })

  describe('normalizeEloBracket', () => {
    it.each([
      ['GOLD', 'GOLD'],
      ['gold', 'GOLD'],
      ['gold_plus', 'GOLD_PLUS'],
      ['ALL', 'ALL'],
    ])('canonicalises %p to %p', (input, expected) => {
      expect(normalizeEloBracket(input)).toBe(expected)
    })

    it.each([null, undefined, '', 'garbage', 'UNRANKED'])(
      'falls back to ALL for %p',
      (input) => {
        expect(normalizeEloBracket(input as string | null | undefined)).toBe(ELO_BRACKET_ALL)
      },
    )
  })

  describe('eloBracketLabel', () => {
    it.each([
      ['ALL', 'All ranks'],
      ['GOLD', 'Gold'],
      ['GOLD_PLUS', 'Gold+'],
      ['DIAMOND_PLUS', 'Diamond+'],
      ['MASTER', 'Master'],
      [null, 'All ranks'],
      ['garbage', 'All ranks'],
    ])('labels %p as %p', (input, expected) => {
      expect(eloBracketLabel(input as string | null)).toBe(expected)
    })
  })
})
