import { describe, expect, it } from 'vitest'
import type { ChampionSummaryResponse } from '~~/shared/types/champions'
import { championLastmodById, toSitemapDay } from '~~/shared/utils/sitemap-lastmod'

function row(championId: number, lastUpdatedAtUtc: string, position = 'MIDDLE'): ChampionSummaryResponse {
  return {
    championId,
    position,
    lastUpdatedAtUtc,
    games: 100,
    wins: 50,
    winRate: 0.5,
    pickRate: 0.1,
    lanePlayRate: 0.9,
    trueMainCount: 3,
    banRate: null,
    tier: 'B',
    tierScore: 0.5,
    patchVersion: '16.16',
    topBuild: null,
  }
}

describe('toSitemapDay', () => {
  it('truncates an instant to its UTC day', () => {
    expect(toSitemapDay('2026-08-26T14:27:33.796428Z')).toBe('2026-08-26')
  })

  it('truncates in UTC, not in the server\'s local time', () => {
    // 23:30 UTC-ish offsets are where a local-time truncation drifts a day.
    expect(toSitemapDay('2026-08-26T23:30:00Z')).toBe('2026-08-26')
    expect(toSitemapDay('2026-08-27T00:30:00Z')).toBe('2026-08-27')
  })

  it('is null for a missing, blank or unparseable value', () => {
    expect(toSitemapDay(null)).toBeNull()
    expect(toSitemapDay(undefined)).toBeNull()
    expect(toSitemapDay('')).toBeNull()
    expect(toSitemapDay('not a date')).toBeNull()
  })
})

describe('championLastmodById', () => {
  it('takes the most recent lane for a champion that flexes', () => {
    const days = championLastmodById([
      row(103, '2026-08-24T10:00:00Z', 'MIDDLE'),
      row(103, '2026-08-26T09:00:00Z', 'TOP'),
      row(103, '2026-08-25T23:59:00Z', 'BOTTOM'),
    ])
    expect(days.get(103)).toBe('2026-08-26')
  })

  it('compares instants before truncating, not day strings', () => {
    // Both rows land on the same day; the later instant must win even though
    // it is listed first, which a string comparison on days would get wrong.
    const days = championLastmodById([
      row(238, '2026-08-26T23:00:00Z', 'MIDDLE'),
      row(238, '2026-08-26T01:00:00Z', 'TOP'),
    ])
    expect(days.get(238)).toBe('2026-08-26')
  })

  it('omits a champion whose timestamps are all unusable rather than guessing', () => {
    const days = championLastmodById([row(11, ''), row(12, 'not a date')])
    expect(days.has(11)).toBe(false)
    expect(days.has(12)).toBe(false)
  })

  it('keeps the usable lane when a sibling row is malformed', () => {
    const days = championLastmodById([
      row(145, 'not a date', 'MIDDLE'),
      row(145, '2026-08-26T08:00:00Z', 'BOTTOM'),
    ])
    expect(days.get(145)).toBe('2026-08-26')
  })

  it('degrades to an empty map when the directory is unavailable', () => {
    expect(championLastmodById(null).size).toBe(0)
    expect(championLastmodById(undefined).size).toBe(0)
    expect(championLastmodById([]).size).toBe(0)
  })
})
