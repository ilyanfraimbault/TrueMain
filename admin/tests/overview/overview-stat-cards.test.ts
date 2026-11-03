import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import type { OverviewStats } from '~~/shared/types/ops'
import OverviewStatCards from '~~/app/components/overview/OverviewStatCards.vue'

// The Overview cards (#2301): the match-side figures are planner estimates and read `≈`;
// unavailable, they read as a dash with the reason, never as zero, while the exact counters
// beside them still render.
const global = {
  stubs: {
    UPageGrid: { template: '<div><slot /></div>' },
    UPageCard: { props: ['title'], template: '<div><span>{{ title }}</span><slot /></div>' },
    USkeleton: { template: '<span>loading</span>' },
  },
}

function stats(overrides: Partial<OverviewStats>): OverviewStats {
  return {
    trackedAccounts: 5200,
    totalMatches: 900000,
    totalParticipants: 9000000,
    candidatesByStatus: { New: 1, Scored: 2, Queued: 3, Processing: 0, Validated: 4 },
    totalMains: 1200,
    totalOtps: 190,
    distinctChampionsWithGames: 172,
    distinctChampionsWithMains: 170,
    matchesLast7Days: 240000,
    matchesLast30Days: 880000,
    estimatesUnknownReason: null,
    computedAtUtc: '2026-10-26T10:00:00Z',
    ...overrides,
  }
}

describe('OverviewStatCards', () => {
  it('labels the match-side figures as estimates and keeps the exact ones bare', () => {
    const wrapper = mount(OverviewStatCards, { props: { stats: stats({}), pending: false }, global })
    const text = wrapper.text()
    expect(text).toContain('≈ 900,000')
    expect(text).toContain('≈ 9,000,000 participants')
    expect(text).toContain('≈ 240,000')
    expect(text).toContain('≈ 880,000')
    expect(text).toContain('5,200')
    expect(text).not.toContain('≈ 5,200')
  })

  it('shows the reason instead of zero when the estimates are unavailable', () => {
    const wrapper = mount(OverviewStatCards, {
      props: {
        stats: stats({
          totalMatches: null,
          totalParticipants: null,
          matchesLast7Days: null,
          matchesLast30Days: null,
          estimatesUnknownReason: 'The planner estimates could not be read.',
        }),
        pending: false,
      },
      global,
    })
    const text = wrapper.text()
    expect(text).not.toContain('≈')
    expect(text).toContain('The planner estimates could not be read.')
    expect(text).toContain('—')
    expect(text).toContain('1,200')
  })

  it('renders skeletons while the totals load', () => {
    const wrapper = mount(OverviewStatCards, { props: { stats: undefined, pending: true }, global })
    expect(wrapper.text()).toContain('loading')
    expect(wrapper.text()).not.toContain('≈')
  })
})
