import { describe, expect, it } from 'vitest'
import type { TierBoundary, TierZone } from '#common/utils/rank-chart-zones'
import {
  boundaryLinePath,
  crestBands,
  scoreY,
  zoneClipPath,
  zoneVisible,
} from '#common/utils/rank-chart-geometry'

const frame = { xs: [0, 50, 100], height: 100, domain: [2700, 3200] as [number, number] }

function boundary(tier: string, scores: number[], measured: boolean[]): TierBoundary {
  return { tier, scores, measured }
}

const master = boundary('MASTER', [2800, 2800, 2800], [true, true, true])
const gm = boundary('GRANDMASTER', [3100, 3000, Infinity], [true, true, false])
const zones: TierZone[] = [
  { tier: 'DIAMOND', lower: null, upper: master },
  { tier: 'MASTER', lower: master, upper: gm },
  { tier: 'GRANDMASTER', lower: gm, upper: null },
]

describe('scoreY', () => {
  it('maps the domain onto the plot, top down', () => {
    expect(scoreY(3200, frame)).toBe(0)
    expect(scoreY(2700, frame)).toBe(100)
    expect(scoreY(2950, frame)).toBe(50)
  })

  it('clamps unbounded and out-of-range scores just past the plot', () => {
    expect(scoreY(Infinity, frame)).toBe(-8)
    expect(scoreY(-Infinity, frame)).toBe(108)
    expect(scoreY(10_000, frame)).toBe(-8)
  })

  it('centres a flat domain', () => {
    expect(scoreY(3000, { ...frame, domain: [3000, 3000] })).toBe(50)
  })
})

describe('zoneClipPath', () => {
  it('runs straight between measured ends and steps at the midpoint otherwise', () => {
    const path = zoneClipPath(zones[2]!, frame)
    // Ceiling: unbounded, along the top. Floor: 3100 → 3000 straight, then a step to off-plot at x = 75.
    expect(path).toBe('M0.0,-8.0L50.0,-8.0L100.0,-8.0L100.0,-8.0L75.0,-8.0L75.0,40.0L50.0,40.0L0.0,20.0Z')
  })
})

describe('zoneVisible', () => {
  it('tells whether any part of a band falls in the plot', () => {
    expect(zones.map(zone => zoneVisible(zone, frame))).toEqual([true, true, true])
    expect(zoneVisible({ tier: 'IRON', lower: null, upper: boundary('BRONZE', [400, 400, 400], [true, true, true]) }, frame)).toBe(false)
  })
})

describe('boundaryLinePath', () => {
  it('draws only where the threshold was measured, ending halfway to an unmeasured snapshot', () => {
    expect(boundaryLinePath(gm, frame)).toBe('M0.0,20.0L0.0,20.0L50.0,40.0L75.0,40.0')
  })

  it('draws a fixed threshold across the plot', () => {
    expect(boundaryLinePath(master, frame)).toBe('M0.0,80.0L0.0,80.0L50.0,80.0L100.0,80.0')
  })

  it('draws nothing out of view', () => {
    expect(boundaryLinePath(boundary('DIAMOND', [2400, 2400, 2400], [true, true, true]), frame)).toBe('')
  })
})

describe('crestBands', () => {
  it('sits each crest on the line where its tier begins', () => {
    const bands = crestBands(zones.slice(1), frame)
    // Master on its fixed floor (y 80), Grandmaster on the cutoff at its first measured snapshot (y 20).
    expect(bands.map(band => [band.tier, Math.round(band.top)])).toEqual([['MASTER', 80], ['GRANDMASTER', 20]])
  })

  it('centres a tier whose floor is out of view on its band', () => {
    const diamondOnly = [{ tier: 'DIAMOND', lower: null, upper: boundary('MASTER', [3000, 3000, 3000], [true, true, true]) }]
    // The band runs from the plot's bottom (y 100) to the Master line (y 40).
    expect(crestBands(diamondOnly, frame).map(band => [band.tier, Math.round(band.top)])).toEqual([['DIAMOND', 70]])
  })

  it('moves a centred crest clear of a line crest, or leaves it out when the plot has no room', () => {
    // Diamond's band is y 80–100: centred at 90, it would cover Master's crest at 80.
    expect(crestBands(zones, frame).map(band => band.tier)).toEqual(['MASTER', 'GRANDMASTER'])
  })

  it('leaves out a band too thin to hold a crest', () => {
    const thin = [{ tier: 'DIAMOND', lower: null, upper: boundary('MASTER', [2705, 2705, 2705], [true, true, true]) }]
    expect(crestBands(thin, frame)).toEqual([])
  })
})
