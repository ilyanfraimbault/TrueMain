import { describe, expect, it } from 'vitest'
import type { TruemainDedication } from '~~/shared/types/dedication'
import {
  dedicationParts,
  dedicationVerdict,
  formatDedicationLastPlayed,
  formatDedicationScore,
} from '~~/app/utils/dedication'

/**
 * A payload shaped like the backend's, overridable per test. Every figure the
 * helpers render comes from the API, so the fixture is the contract — nothing
 * here recomputes a score.
 */
function dedication(overrides: Partial<TruemainDedication> = {}): TruemainDedication {
  return {
    score: 50.7,
    championId: 64,
    isOtp: false,
    playRate: 0.3,
    championGames: 15,
    recentGames: 50,
    masteryPoints: 1_400_000,
    masteryRank: 1,
    daysSinceLastPlayed: 2,
    parts: [
      { key: 'playRate', points: 11.25, maxPoints: 55 },
      { key: 'mastery', points: 24.416, maxPoints: 30 },
      { key: 'masteryRank', points: 15, maxPoints: 15 },
    ],
    ...overrides,
  }
}

describe('formatDedicationScore', () => {
  it('prints a whole number', () => {
    expect(formatDedicationScore(62.9)).toBe('63')
    expect(formatDedicationScore(62.4)).toBe('62')
  })

  it('rounds a half up', () => {
    expect(formatDedicationScore(62.5)).toBe('63')
  })

  it('covers both ends of the scale', () => {
    expect(formatDedicationScore(0)).toBe('0')
    expect(formatDedicationScore(100)).toBe('100')
  })
})

describe('dedicationVerdict', () => {
  it('reads the same isOtp flag as the OTP badge', () => {
    expect(dedicationVerdict(dedication({ isOtp: true }))).toBe('OTP')
    expect(dedicationVerdict(dedication({ isOtp: false }))).toBe('Main')
  })

  it('does not derive the verdict from the score', () => {
    // A high score without the flag is still a Main, a low one with it still
    // an OTP: the badge, the filter and the verdict must never disagree.
    expect(dedicationVerdict(dedication({ score: 95, isOtp: false }))).toBe('Main')
    expect(dedicationVerdict(dedication({ score: 40, isOtp: true }))).toBe('OTP')
  })
})

describe('formatDedicationLastPlayed', () => {
  it.each([
    [0, 'Last played today'],
    [1, 'Last played yesterday'],
    [12, 'Last played 12 days ago'],
  ])('phrases %p days', (days, expected) => {
    expect(formatDedicationLastPlayed(days)).toBe(expected)
  })

  it('says nothing when the mastery has not been read', () => {
    expect(formatDedicationLastPlayed(null)).toBeNull()
  })
})

describe('dedicationParts', () => {
  it('keeps the payload order and labels', () => {
    expect(dedicationParts(dedication()).map(part => part.label))
      .toEqual(['Play rate', 'Mastery', 'Mastery rank'])
  })

  it('adds up exactly to the displayed score', () => {
    // 11.25 + 24.416 + 15 rounds part by part to 11 + 24 + 15 = 50, one short
    // of the 51 printed above them; the largest remainder takes the point.
    const parts = dedicationParts(dedication())
    expect(parts.map(part => part.points)).toEqual([11, 25, 15])
    expect(parts.reduce((sum, part) => sum + part.points, 0)).toBe(51)
  })

  it('shows each part against its own maximum', () => {
    expect(dedicationParts(dedication()).map(part => part.maxPoints)).toEqual([55, 30, 15])
  })

  it('prints the raw facts', () => {
    const [playRate, mastery, rank] = dedicationParts(dedication())
    expect(playRate!.detail).toBe('30% of last 50 ranked')
    expect(mastery!.detail).toBe('1.4M points')
    expect(rank!.detail).toBe('their most-played ever')
  })

  it('names a lower mastery rank', () => {
    const [, , rank] = dedicationParts(dedication({ masteryRank: 3 }))
    expect(rank!.detail).toBe('#3 in their mastery')
  })

  it('says the mastery is pending rather than printing a zero', () => {
    const [, mastery, rank] = dedicationParts(dedication({
      masteryPoints: null,
      masteryRank: null,
      parts: [
        { key: 'playRate', points: 55, maxPoints: 55 },
        { key: 'mastery', points: 0, maxPoints: 30 },
        { key: 'masteryRank', points: 0, maxPoints: 15 },
      ],
      score: 55,
    }))
    expect(mastery!.detail).toBe('not checked yet')
    expect(rank!.detail).toBe('not checked yet')
  })

  it('tells a measured absence from a pending read', () => {
    const [, mastery, rank] = dedicationParts(dedication({ masteryPoints: 0, masteryRank: null }))
    expect(mastery!.detail).toBe('no mastery')
    expect(rank!.detail).toBe('no mastery')
  })
})
