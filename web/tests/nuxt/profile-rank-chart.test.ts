import { flushPromises } from '@vue/test-utils'
import { mountSuspended } from '@nuxt/test-utils/runtime'
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { ProfileRankChart } from '#components'
import type { ApexCutoff } from '~~/shared/types/apex-cutoffs'
import type { RankHistoryEntry } from '~~/shared/types/rank-history'
import { rankChartPoints } from '#common/utils/rank-chart'

/**
 * The ranked card's LP curve (#2174), mounted: one clipped copy of the curve per
 * tier band in view, the thresholds as dashed lines, the crests beside it.
 */

const DAY = 24 * 60 * 60 * 1000
const START = Date.parse('2026-09-01T12:00:00Z')
const day = (i: number) => new Date(START + i * DAY).toISOString()

const history: RankHistoryEntry[] = [
  { capturedAtUtc: day(0), tier: 'GRANDMASTER', division: 'I', leaguePoints: 520 },
  { capturedAtUtc: day(1), tier: 'MASTER', division: 'I', leaguePoints: 300 },
  { capturedAtUtc: day(2), tier: 'MASTER', division: 'I', leaguePoints: 40 },
  { capturedAtUtc: day(3), tier: 'DIAMOND', division: 'I', leaguePoints: 70 },
]
const cutoffs: ApexCutoff[] = [0, 1, 2, 3].map(i => ({
  dateUtc: day(i).slice(0, 10),
  grandmasterMinLp: 400,
  challengerMinLp: 900,
}))

beforeAll(() => {
  // happy-dom never lays anything out: report a card-width box on observe.
  vi.stubGlobal('ResizeObserver', class {
    constructor(private readonly callback: ResizeObserverCallback) {}
    observe() {
      this.callback([{ contentRect: { width: 240 } } as ResizeObserverEntry], this as unknown as ResizeObserver)
    }

    disconnect() {}
  })
})
afterAll(() => vi.unstubAllGlobals())

describe('ProfileRankChart', () => {
  it('clips the curve per tier band and draws the thresholds in view', async () => {
    const wrapper = await mountSuspended(ProfileRankChart, {
      props: { points: rankChartPoints(history), cutoffs },
    })
    await flushPromises()

    const clips = wrapper.findAll('clipPath')
    expect(clips.length).toBeGreaterThanOrEqual(3)
    expect(wrapper.findAll('g[clip-path] path[stroke]')).toHaveLength(clips.length)
    // Master's floor and the Grandmaster cutoff; Diamond's floor sits below the range.
    expect(wrapper.findAll('path[stroke-dasharray]').map(path => path.attributes('stroke'))).toEqual(['#f0abfc', '#fca5a5'])
    expect(wrapper.text()).not.toContain('LP')
  })

  it('shows the hovered snapshot', async () => {
    const wrapper = await mountSuspended(ProfileRankChart, {
      props: { points: rankChartPoints(history), cutoffs },
    })
    await flushPromises()

    await wrapper.find('svg').trigger('mousemove', { offsetX: 0 })
    expect(wrapper.find('circle').exists()).toBe(true)
    expect(wrapper.text()).toContain('520')
  })

  it('draws no apex line without a measured cutoff', async () => {
    const wrapper = await mountSuspended(ProfileRankChart, {
      props: { points: rankChartPoints(history.slice(0, 2)) },
    })
    await flushPromises()

    const lines = wrapper.findAll('path[stroke-dasharray]').map(path => path.attributes('stroke'))
    expect(lines).not.toContain('#fca5a5')
  })
})
