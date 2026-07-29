import { describe, expect, it } from 'vitest'
import type { ChampionSummaryResponse } from '../../shared/types/champions'
import type { ProfileMainChampion } from '../../shared/types/profile'
import {
  parseOgChampionId,
  selectChampionSummaryRow,
  selectSignatureMain,
} from '../../shared/utils/og-card'

/**
 * The share cards (#926) are rendered server-side, away from the page, and read
 * as a screenshot of it. The failure mode that matters is therefore not a crash
 * but a *plausible wrong number* — a real measurement printed under the wrong
 * lane, or a placeholder that reads like a measurement. These tests pin the two
 * selections that could produce one.
 */

function summary(overrides: Partial<ChampionSummaryResponse>): ChampionSummaryResponse {
  return {
    championId: 103,
    games: 1000,
    wins: 520,
    winRate: 0.52,
    pickRate: 0.08,
    lanePlayRate: 0.9,
    trueMainCount: 20,
    banRate: 0.12,
    tier: 'A',
    position: 'MIDDLE',
    patchVersion: '15.13',
    lastUpdatedAtUtc: '2026-07-01T00:00:00Z',
    topBuild: null,
    ...overrides,
  }
}

function main(overrides: Partial<ProfileMainChampion>): ProfileMainChampion {
  return {
    championId: 103,
    games: 100,
    playRate: 0.5,
    primaryPosition: 'MIDDLE',
    isOtp: false,
    ...overrides,
  }
}

describe('selectChampionSummaryRow', () => {
  const rows = [
    summary({ championId: 103, position: 'MIDDLE', games: 900, winRate: 0.52 }),
    summary({ championId: 103, position: 'UTILITY', games: 300, winRate: 0.47 }),
    summary({ championId: 1, position: 'MIDDLE', games: 5000, winRate: 0.51 }),
  ]

  it('returns the pinned lane when the shared URL had one', () => {
    expect(selectChampionSummaryRow(rows, 103, 'UTILITY')?.winRate).toBe(0.47)
  })

  it('accepts a lowercase pinned lane (query params are not normalised upstream)', () => {
    expect(selectChampionSummaryRow(rows, 103, 'utility')?.position).toBe('UTILITY')
  })

  it('falls back to the most-played lane when the pinned lane has no row', () => {
    // Safe only because the card captions the row's own position: it says
    // "Middle" over Middle's numbers, never "Jungle" over them. Matches the
    // page, whose 404 fallback drops a dead lane filter the same way.
    const fallback = selectChampionSummaryRow(rows, 103, 'JUNGLE')
    expect(fallback?.position).toBe('MIDDLE')
    expect(fallback?.winRate).toBe(0.52)
  })

  it('falls back to the most-played lane when no lane is pinned', () => {
    // Same slice `GET /champions/{id}` defaults to, so the card agrees with
    // the page it links to.
    expect(selectChampionSummaryRow(rows, 103)?.position).toBe('MIDDLE')
  })

  it('resolves ties to the first row, so the same URL always yields the same card', () => {
    const tied = [
      summary({ championId: 7, position: 'MIDDLE', games: 500 }),
      summary({ championId: 7, position: 'TOP', games: 500 }),
    ]
    expect(selectChampionSummaryRow(tied, 7)?.position).toBe('MIDDLE')
  })

  it('returns null for a champion with no rows, and for an absent payload', () => {
    expect(selectChampionSummaryRow(rows, 999)).toBeNull()
    expect(selectChampionSummaryRow([], 103)).toBeNull()
    expect(selectChampionSummaryRow(null, 103)).toBeNull()
    expect(selectChampionSummaryRow(undefined, 103)).toBeNull()
  })
})

describe('selectSignatureMain', () => {
  it('picks the most-played champion', () => {
    const mains = [main({ championId: 1, games: 40 }), main({ championId: 2, games: 120 })]
    expect(selectSignatureMain(mains)?.championId).toBe(2)
  })

  it('returns null when the player has no classified main', () => {
    expect(selectSignatureMain([])).toBeNull()
    expect(selectSignatureMain(null)).toBeNull()
    expect(selectSignatureMain(undefined)).toBeNull()
  })
})

describe('parseOgChampionId', () => {
  it('accepts a plain positive integer', () => {
    expect(parseOgChampionId('103')).toBe(103)
  })

  it('rejects everything that is not one, instead of coercing it', () => {
    // The value reaches us from a URL segment; `Number('')` is 0 and
    // `Number(' 12 ')` is 12, either of which would turn a hand-crafted OG URL
    // into an upstream call we never meant to make.
    for (const raw of ['', ' 12 ', '0', '-4', '1e3', '12.5', 'abc', '12345678', null, undefined]) {
      expect(parseOgChampionId(raw)).toBeNull()
    }
  })
})
