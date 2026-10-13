import { describe, expect, it } from 'vitest'
import type { RankHistoryEntry } from '#shared/types/rank-history'
import {
  areaFillOf,
  crestBands,
  lineStrokeOf,
  lpDeltaSince,
  rankChartPoints,
  rankChartYDomain,
  tierGradientStops,
} from '#common/utils/rank-chart'
import { tierHex } from '#common/utils/tiers'

const NOW = Date.parse('2026-10-13T12:00:00Z')
const DAY = 24 * 60 * 60 * 1000

function snapshot(daysAgo: number, tier: string, division: string, leaguePoints: number): RankHistoryEntry {
  return { capturedAtUtc: new Date(NOW - daysAgo * DAY).toISOString(), tier, division, leaguePoints }
}

const climb = rankChartPoints([
  snapshot(40, 'GOLD', 'I', 80),
  snapshot(20, 'PLATINUM', 'IV', 10),
  snapshot(5, 'PLATINUM', 'IV', 60),
  snapshot(1, 'PLATINUM', 'III', 20),
])

describe('lpDeltaSince', () => {
  it('compares the latest score to the last snapshot before the cutoff', () => {
    expect(lpDeltaSince(climb, 7, NOW)).toBe(climb[3]!.score - climb[1]!.score)
    expect(lpDeltaSince(climb, 30, NOW)).toBe(climb[3]!.score - climb[0]!.score)
  })

  it('falls back to the earliest snapshot when none is older than the cutoff', () => {
    expect(lpDeltaSince(climb.slice(2), 30, NOW)).toBe(climb[3]!.score - climb[2]!.score)
  })

  it('has nothing to compare with a single snapshot', () => {
    expect(lpDeltaSince(climb.slice(0, 1), 7, NOW)).toBeNull()
  })
})

describe('tierGradientStops', () => {
  it('puts one hard-edged stop pair per tier run, meeting halfway between snapshots', () => {
    expect(tierGradientStops(climb)).toEqual([
      { offset: '0.000%', color: tierHex('GOLD') },
      { offset: '16.667%', color: tierHex('GOLD') },
      { offset: '16.667%', color: tierHex('PLATINUM') },
      { offset: '100.000%', color: tierHex('PLATINUM') },
    ])
  })

  it('draws a single snapshot in a plain colour', () => {
    const one = climb.slice(0, 1)
    expect(tierGradientStops(one)).toEqual([])
    expect(areaFillOf(one, 'GOLD', 'url(#g)')).toBe(tierHex('GOLD'))
    expect(lineStrokeOf(one, 'GOLD', 'url(#g)')).toBe(tierHex('GOLD'))
  })

  it('paints a multi-tier history with the gradient', () => {
    expect(lineStrokeOf(climb, 'PLATINUM', 'url(#g)')).toBe('url(#g)')
    expect(areaFillOf(climb, 'PLATINUM', 'url(#g)')).toBe('url(#g)')
  })
})

describe('crestBands and rankChartYDomain', () => {
  it('stacks one crest per tier in climb order', () => {
    expect(crestBands(climb).map(band => band.tier)).toEqual(['GOLD', 'PLATINUM'])
  })

  it('pads the range so the line never hugs an edge', () => {
    const [min, max] = rankChartYDomain(climb)
    const scores = climb.map(point => point.score)
    expect(min).toBeLessThan(Math.min(...scores))
    expect(max).toBeGreaterThan(Math.max(...scores))
    expect(rankChartYDomain([])).toEqual([0, 400])
  })
})
