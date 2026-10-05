import { describe, expect, it } from 'vitest'
import type { PaceBenchmarkResponse } from '~/utils/pace-benchmark'
import { paceReference, tierLabel } from '~/utils/pace-benchmark'

const quartiles = (p25: number, median: number, p75: number) => ({ p25, median, p75 })

const benchmark: PaceBenchmarkResponse = {
  position: 'MIDDLE',
  patches: ['16.19'],
  minSamples: 50,
  tiers: [
    {
      tier: 'DIAMOND',
      minutes: [
        { minute: 9, samples: 40, cs: null, goldEarned: null },
        { minute: 10, samples: 400, cs: quartiles(60, 70, 80), goldEarned: quartiles(3500, 4000, 4500) },
      ],
    },
  ],
}

describe('paceReference', () => {
  it('compares each figure to the tier median at the same minute, as a per-minute rate', () => {
    const reference = paceReference(benchmark, 'DIAMOND', { minute: 10, cs: 85, gold: 4000 })
    expect(reference?.tier).toBe('DIAMOND')
    expect(reference?.cs).toEqual({ median: 7, standing: 'above' })
    expect(reference?.gold).toEqual({ median: 400, standing: 'within' })
  })

  it('reads a value under the first quartile as below', () => {
    expect(paceReference(benchmark, 'diamond', { minute: 10, cs: 50, gold: 3000 })?.cs?.standing).toBe('below')
  })

  it('shows no comparison under the sample floor, but keeps the served minutes on the curve', () => {
    const reference = paceReference(benchmark, 'DIAMOND', { minute: 9, cs: 60, gold: 3600 })
    expect(reference?.cs).toBeNull()
    expect(reference?.gold).toBeNull()
    expect(reference?.csCurve).toEqual([{ minute: 10, value: 7 }])
  })

  it('is nothing without a tier, a sample, or data for the tier', () => {
    expect(paceReference(benchmark, null, { minute: 10, cs: 70, gold: 4000 })).toBeNull()
    expect(paceReference(benchmark, 'DIAMOND', null)).toBeNull()
    expect(paceReference(benchmark, 'GOLD', { minute: 10, cs: 70, gold: 4000 })).toBeNull()
    expect(paceReference(null, 'DIAMOND', { minute: 10, cs: 70, gold: 4000 })).toBeNull()
  })
})

describe('tierLabel', () => {
  it('writes a tier as a reader would', () => {
    expect(tierLabel('GRANDMASTER')).toBe('Grandmaster')
  })
})
