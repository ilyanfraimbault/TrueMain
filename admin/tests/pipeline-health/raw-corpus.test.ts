import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import type { RawDataFreshness } from '~~/shared/types/ops'
import HealthRawCorpus from '~~/app/components/health/HealthRawCorpus.vue'

// The raw-corpus card (#2132): the two totals are planner estimates and can be unavailable on
// their own. A missing count must read as a dash with the reason, never as zero, and must not
// take the per-platform freshness down with it.
const global = {
  stubs: {
    UCard: { template: '<div><slot name="header" /><slot /></div>' },
  },
}

const platforms = [
  { platformId: 'EUW1', latestMatchStartAtUtc: null, latestPatchVersion: '16.4' },
]

function rawData(overrides: Partial<RawDataFreshness>): RawDataFreshness {
  return {
    queueId: 420,
    rawMatchCount: 120000,
    rawParticipantCount: 1200000,
    countsUnknownReason: null,
    platforms,
    ...overrides,
  }
}

describe('HealthRawCorpus', () => {
  it('labels the measured totals as estimates', () => {
    const wrapper = mount(HealthRawCorpus, { props: { rawData: rawData({}) }, global })
    expect(wrapper.text()).toContain('≈ 1,200,000')
    expect(wrapper.text()).toContain('not exact counts')
  })

  it('shows the reason instead of zero when the totals are unavailable', () => {
    const wrapper = mount(HealthRawCorpus, {
      props: {
        rawData: rawData({ rawMatchCount: null, rawParticipantCount: null, countsUnknownReason: 'internal error' }),
      },
      global,
    })
    expect(wrapper.text()).toContain('Counts unavailable: internal error')
    expect(wrapper.text()).not.toContain('≈')
    expect(wrapper.text()).toContain('EUW1')
  })
})
