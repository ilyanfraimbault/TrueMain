import type { RiotApiHeadroom } from '~~/shared/types/ops'
import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ProcessesRiotApiHeadroom from '~~/app/components/processes/ProcessesRiotApiHeadroom.vue'

// Pins what an operator reads off the budget-headroom card (#1894): the headline is the
// binding host's figure, each routing host gets its own row against its own limit, and
// the absent state names why no estimate is shown.

const global = {
  stubs: {
    UCard: { template: '<div class="card"><slot name="header" /><slot /></div>' },
    PanelTitle: { props: ['title'], template: '<div class="panel-title">{{ title }}</div>' },
  },
}

const sufficient: RiotApiHeadroom = {
  sufficientData: true,
  observedWindowHours: 168,
  requiredWindowHours: 24,
  trackedAccounts: 100,
  additionalAccountsHeadroom: 44,
  bindingRoute: 'europe',
  routes: [
    {
      route: 'europe',
      kind: 'regional',
      observedCallsPerDay: 50_000,
      callsPerAccountPerDay: 500,
      bindingLimit: { limit: 100, windowSeconds: 120, maxCallsPerDay: 72_000 },
      spareCallsPerDay: 22_000,
      additionalAccountsHeadroom: 44,
    },
    {
      route: 'kr',
      kind: 'platform',
      observedCallsPerDay: 1_000,
      callsPerAccountPerDay: 10,
      bindingLimit: null,
      spareCallsPerDay: null,
      additionalAccountsHeadroom: null,
    },
  ],
}

function insufficient(overrides: Partial<RiotApiHeadroom>): RiotApiHeadroom {
  return {
    sufficientData: false,
    observedWindowHours: 2,
    requiredWindowHours: 24,
    trackedAccounts: 100,
    additionalAccountsHeadroom: null,
    bindingRoute: null,
    routes: [],
    ...overrides,
  }
}

describe('ProcessesRiotApiHeadroom', () => {
  it('headlines the binding host and lists every host against its own limit', () => {
    const wrapper = mount(ProcessesRiotApiHeadroom, { props: { headroom: sufficient }, global })
    const text = wrapper.text()

    expect(text).toContain('≈ 44')
    expect(text).toMatch(/bound by\s*europe\s*\(regional\)/)

    const rows = wrapper.findAll('tbody tr')
    expect(rows).toHaveLength(2)
    expect(rows[0]!.text()).toContain('europe')
    expect(rows[0]!.text()).toContain('100 / 2m')
    expect(rows[0]!.classes()).toContain('text-highlighted')
    expect(rows[1]!.text()).toContain('no limit header')
    expect(rows[1]!.text()).toContain('—')
  })

  it('states the per-host history still missing', () => {
    const wrapper = mount(ProcessesRiotApiHeadroom, { props: { headroom: insufficient({}) }, global })

    expect(wrapper.text()).toContain('2.0h of per-host history, 24h needed')
    expect(wrapper.find('table').exists()).toBe(false)
  })

  it('names a missing limit header once the history is long enough', () => {
    const wrapper = mount(ProcessesRiotApiHeadroom, {
      props: { headroom: insufficient({ observedWindowHours: 168 }) },
      global,
    })

    expect(wrapper.text()).toContain('No host has returned a rate-limit header yet.')
  })
})
