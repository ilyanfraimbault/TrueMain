import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed } from 'vue'
import type { RankedQueue } from '../app/types/record'
import DashboardFlexStanding from '../app/components/dashboard/DashboardFlexStanding.vue'

const global = { stubs: { RankIcon: { props: ['tier'], template: '<i class="crest" :data-tier="tier" />' } } }

const flex = (overrides: Partial<RankedQueue> = {}): RankedQueue => ({
  queueType: 'RANKED_FLEX_SR',
  tier: 'PLATINUM',
  division: 'I',
  leaguePoints: 12,
  wins: 14,
  losses: 11,
  isProvisional: false,
  provisionalGamesRemaining: 0,
  ...overrides,
})

beforeEach(() => vi.stubGlobal('computed', computed))
afterEach(() => vi.unstubAllGlobals())

describe('DashboardFlexStanding', () => {
  it('shows the crest, the rank with its LP and the record', () => {
    const wrapper = mount(DashboardFlexStanding, { props: { queue: flex() }, global })
    expect(wrapper.find('.crest').attributes('data-tier')).toBe('PLATINUM')
    expect(wrapper.text()).toContain('Platinum I')
    expect(wrapper.text()).toContain('12 LP')
    expect(wrapper.text()).toContain('14W 11L · 56%')
  })

  it('reads placements as such, without a crest', () => {
    const wrapper = mount(DashboardFlexStanding, {
      props: { queue: flex({ tier: '', division: '', wins: 2, losses: 1, isProvisional: true }) },
      global,
    })
    expect(wrapper.find('.crest').exists()).toBe(false)
    expect(wrapper.text()).toContain('Placements · 3 played')
  })

  it('renders nothing for a player unranked in Flex', () => {
    expect(mount(DashboardFlexStanding, { props: { queue: flex({ tier: 'NONE', wins: 0, losses: 0 }) }, global }).html()).not.toContain('flex-standing')
    expect(mount(DashboardFlexStanding, { props: { queue: null }, global }).html()).not.toContain('flex-standing')
  })
})
