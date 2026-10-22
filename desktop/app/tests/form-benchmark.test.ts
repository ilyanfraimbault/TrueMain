import { describe, expect, it } from 'vitest'
import type { FormBenchmarkResponse } from '~/utils/form-benchmark'
import { formReference } from '~/utils/form-benchmark'

const quartiles = (median: number) => ({ p25: median - 1, median, p75: median + 1 })

const benchmark: FormBenchmarkResponse = {
  position: 'MIDDLE',
  patches: ['16.19', '16.18'],
  minSamples: 50,
  tiers: [
    { tier: 'GOLD', samples: 12, kda: null, killParticipation: null, csPerMinute: null, deaths: null, visionPerMinute: null },
    {
      tier: 'DIAMOND',
      samples: 840,
      kda: quartiles(2.9),
      killParticipation: quartiles(0.54),
      csPerMinute: quartiles(7.6),
      deaths: quartiles(4.8),
      visionPerMinute: quartiles(0.9),
    },
  ],
}

describe('formReference', () => {
  it('reads the medians of the player\'s tier, whatever the case of the client\'s tier', () => {
    expect(formReference(benchmark, 'diamond')).toEqual({
      tier: 'DIAMOND',
      position: 'MIDDLE',
      samples: 840,
      medians: { kda: 2.9, killParticipation: 0.54, csPerMinute: 7.6, deaths: 4.8 },
    })
  })

  it('has nothing to set against under the sample floor, without a tier, or for an absent tier', () => {
    expect(formReference(benchmark, 'GOLD')).toBeNull()
    expect(formReference(benchmark, null)).toBeNull()
    expect(formReference(benchmark, 'IRON')).toBeNull()
    expect(formReference(null, 'DIAMOND')).toBeNull()
  })

  it('leaves out only the figures under the floor', () => {
    const partial: FormBenchmarkResponse = { ...benchmark, tiers: [{ ...benchmark.tiers[1]!, deaths: null }] }
    expect(formReference(partial, 'DIAMOND')?.medians).toEqual({ kda: 2.9, killParticipation: 0.54, csPerMinute: 7.6 })
  })
})
