import { describe, expect, it } from 'vitest'
import type { PlatformBalance, RegionBalance } from '~~/shared/types/ops'
import { chartedPlatforms, claimLabel, regionDailyRows, windowDays } from '~~/shared/utils/region-balance'

// The region-balance chart (#1153) exists to make a drift visible as a trend. What could hide
// one: a day nothing was ingested silently dropped from the axis, or a platform the claim no
// longer covers vanishing from the chart while its last bars still sit in the window.

function platform(platformId: string, inClaim: boolean | null): PlatformBalance {
  return {
    platformId,
    inClaim,
    accounts: 0,
    activeMainAccounts: 0,
    matchesInWindow: 0,
    matchShare: null,
    meanCoverageDeficit: null,
    championsBelowTarget: null,
    championsBelowTargetShare: null,
    claimShare: null,
  }
}

function balance(overrides: Partial<RegionBalance> = {}): RegionBalance {
  return {
    measuredAtUtc: '2026-10-02T12:00:00Z',
    windowDays: 3,
    windowStartUtc: '2026-09-30T00:00:00Z',
    targetMainsPerChampion: 50,
    claimPlatforms: ['EUW1', 'KR'],
    configurationCapturedAtUtc: null,
    coverageUnknownReason: null,
    championUniverse: 0,
    platforms: [platform('EUW1', true), platform('KR', true), platform('NA1', false)],
    dailyMatches: [
      { day: '2026-09-30', platformId: 'EUW1', matches: 40 },
      { day: '2026-10-02', platformId: 'KR', matches: 5 },
    ],
    unknownReason: null,
    ...overrides,
  }
}

describe('windowDays', () => {
  it('lists every UTC day of the window, oldest first', () => {
    expect(windowDays(balance())).toEqual(['2026-09-30', '2026-10-01', '2026-10-02'])
  })

  it('is empty on an unparseable start rather than inventing days', () => {
    expect(windowDays(balance({ windowStartUtc: 'nope' }))).toEqual([])
  })
})

describe('regionDailyRows', () => {
  it('keeps a day nobody ingested on, as zeros', () => {
    const rows = regionDailyRows(balance(), ['EUW1', 'KR'])
    expect(rows).toEqual([
      { label: '2026-09-30', EUW1: 40, KR: 0 },
      { label: '2026-10-01', EUW1: 0, KR: 0 },
      { label: '2026-10-02', EUW1: 0, KR: 5 },
    ])
  })
})

describe('chartedPlatforms', () => {
  it('drops a platform outside the claim that ingested nothing', () => {
    expect(chartedPlatforms(balance())).toEqual(['EUW1', 'KR'])
  })

  it('keeps a platform outside the claim that still ingested in the window', () => {
    const narrowed = balance({
      dailyMatches: [{ day: '2026-09-30', platformId: 'NA1', matches: 3 }],
    })
    expect(chartedPlatforms(narrowed)).toEqual(['EUW1', 'KR', 'NA1'])
  })

  it('keeps every platform when the claim scope is unknown', () => {
    const unknown = balance({ platforms: [platform('EUW1', null), platform('NA1', null)], dailyMatches: [] })
    expect(chartedPlatforms(unknown)).toEqual(['EUW1', 'NA1'])
  })
})

describe('claimLabel', () => {
  it('tells an unknown scope from a platform the claim skips', () => {
    expect(claimLabel({ inClaim: null })).toBe('unknown')
    expect(claimLabel({ inClaim: false })).toBe('not claimed')
    expect(claimLabel({ inClaim: true })).toBe('in claim')
  })
})
