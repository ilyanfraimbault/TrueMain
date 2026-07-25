import { describe, expect, it } from 'vitest'
import type { TruemainDedication } from '~~/shared/types/dedication'
import {
  dedicationComponents,
  dedicationTier,
  dedicationTierColor,
  describeDedication,
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
    score: 62.9,
    championId: 64,
    commitment: 0.205,
    span: 1,
    volume: 0.98,
    recency: 0.936,
    playRate: 0.3,
    careerGames: 180,
    patchSpan: 7,
    daysSinceLastGame: 2,
    ...overrides,
  }
}

describe('formatDedicationScore', () => {
  it('drops the decimal for the compact leaderboard cell', () => {
    expect(formatDedicationScore(62.9)).toBe('63')
    expect(formatDedicationScore(62.4)).toBe('62')
  })

  it('rounds a half up', () => {
    expect(formatDedicationScore(62.5)).toBe('63')
  })

  it('leaves a whole score alone', () => {
    expect(formatDedicationScore(74)).toBe('74')
  })

  it('covers both ends of the scale', () => {
    expect(formatDedicationScore(0)).toBe('0')
    expect(formatDedicationScore(100)).toBe('100')
  })
})

describe('dedicationTier', () => {
  // The bands are presentation-only, but an off-by-one on an edge silently
  // relabels every player sitting on it — so each boundary is pinned exactly,
  // along with the value just below it.
  it.each([
    [100, 'Devoted'],
    [85, 'Devoted'],
    [84.9, 'Committed'],
    [70, 'Committed'],
    [69.9, 'Invested'],
    [50, 'Invested'],
    [49.9, 'Casual'],
    [30, 'Casual'],
    [29.9, 'Dabbling'],
    [0, 'Dabbling'],
  ])('labels %p as %s', (score, expected) => {
    expect(dedicationTier(score)).toBe(expected)
  })

  it('covers the whole 0..100 range with no gap', () => {
    for (let score = 0; score <= 100; score += 0.5) {
      expect(dedicationTier(score)).toBeTruthy()
    }
  })
})

describe('dedicationTierColor', () => {
  // Mirrors the `TierBadge` S..D scale (best → worst) at the same
  // boundaries as `dedicationTier`, so the word and the colour never drift.
  it.each([
    [100, 'text-tier-s'],
    [85, 'text-tier-s'],
    [84.9, 'text-tier-a'],
    [70, 'text-tier-a'],
    [69.9, 'text-tier-b'],
    [50, 'text-tier-b'],
    [49.9, 'text-tier-c'],
    [30, 'text-tier-c'],
    [29.9, 'text-tier-d'],
    [0, 'text-tier-d'],
  ])('colours %p as %s', (score, expected) => {
    expect(dedicationTierColor(score)).toBe(expected)
  })
})

describe('formatDedicationLastPlayed', () => {
  it('distinguishes "never tracked" from "played today"', () => {
    // Null means no aggregated game exists yet; it must not read as a fresh 0.
    expect(formatDedicationLastPlayed(null)).toBe('no tracked game yet')
    expect(formatDedicationLastPlayed(0)).toBe('played today')
  })

  it('uses the singular for exactly one day', () => {
    expect(formatDedicationLastPlayed(1)).toBe('played yesterday')
  })

  it('uses the plural from two days out', () => {
    expect(formatDedicationLastPlayed(2)).toBe('played 2 days ago')
    expect(formatDedicationLastPlayed(90)).toBe('played 90 days ago')
  })

  it('treats a negative day count as today rather than in the future', () => {
    // Clock skew between the Riot game timestamp and the API host.
    expect(formatDedicationLastPlayed(-3)).toBe('played today')
  })
})

describe('dedicationComponents', () => {
  it('lists the four components heaviest weight first', () => {
    expect(dedicationComponents(dedication()).map(c => c.key))
      .toEqual(['commitment', 'span', 'volume', 'recency'])
  })

  it('passes the normalised values through untouched', () => {
    const components = dedicationComponents(dedication())
    expect(components.map(c => c.value)).toEqual([0.205, 1, 0.98, 0.936])
  })

  it('renders the play rate as a whole percentage', () => {
    expect(dedicationComponents(dedication({ playRate: 0.3 }))[0]!.detail)
      .toBe('30% of recent ranked games')
    expect(dedicationComponents(dedication({ playRate: 0.856 }))[0]!.detail)
      .toBe('86% of recent ranked games')
  })

  it('uses the singular for a single tracked patch', () => {
    expect(dedicationComponents(dedication({ patchSpan: 1 }))[1]!.detail)
      .toBe('1 tracked patch')
  })

  it('uses the plural for any other patch count, including none', () => {
    expect(dedicationComponents(dedication({ patchSpan: 2 }))[1]!.detail)
      .toBe('2 tracked patches')
    expect(dedicationComponents(dedication({ patchSpan: 0 }))[1]!.detail)
      .toBe('0 tracked patches')
  })

  it('groups thousands in the career game count', () => {
    expect(dedicationComponents(dedication({ careerGames: 1234 }))[2]!.detail)
      .toBe('1,234 tracked games')
    expect(dedicationComponents(dedication({ careerGames: 0 }))[2]!.detail)
      .toBe('0 tracked games')
  })

  it('phrases recency through formatDedicationLastPlayed', () => {
    expect(dedicationComponents(dedication({ daysSinceLastGame: 1 }))[3]!.detail)
      .toBe('played yesterday')
    expect(dedicationComponents(dedication({ daysSinceLastGame: null }))[3]!.detail)
      .toBe('no tracked game yet')
  })
})

describe('describeDedication', () => {
  it('leads with the score to one decimal and the champion', () => {
    const [header] = describeDedication(dedication({ score: 62.9 }), 'Kha\'Zix').split('\n')
    expect(header).toBe('Dedication 62.9/100 · Kha\'Zix')
  })

  it('keeps the trailing zero on a whole score', () => {
    const [header] = describeDedication(dedication({ score: 74 }), 'Ahri').split('\n')
    expect(header).toBe('Dedication 74.0/100 · Ahri')
  })

  it('lists every component under the header as percent plus raw figure', () => {
    const lines = describeDedication(
      dedication({ commitment: 0.205, span: 1, volume: 0.98, recency: 0.936 }),
      'Kha\'Zix',
    ).split('\n')

    expect(lines).toHaveLength(5)
    expect(lines[1]).toBe('Play rate 21% — 30% of recent ranked games')
    expect(lines[2]).toBe('Span 100% — 7 tracked patches')
    expect(lines[3]).toBe('Volume 98% — 180 tracked games')
    expect(lines[4]).toBe('Recency 94% — played 2 days ago')
  })

  it('renders a champion the static list could not resolve without throwing', () => {
    // The row falls back to `#{id}` when the champion list has not loaded.
    expect(describeDedication(dedication(), '#64')).toContain('· #64')
  })
})
