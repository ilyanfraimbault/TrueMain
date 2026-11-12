import { mountSuspended } from '@nuxt/test-utils/runtime'
import { describe, expect, it } from 'vitest'
import { ChampionTrendChart, HomeDesktopBetaPanel } from '#components'
import type { ChampionTrendPoint } from '#shared/contract/api'

/**
 * #2552: rates are whole percentages on every surface, and the home page's app
 * banner is a plain heading beside a capture framed by its ring alone.
 */

const points: ChampionTrendPoint[] = [
  { patch: '16.21', winRate: 0.506, pickRate: 0.0449, banRate: 0.121, games: 1200 },
  { patch: '16.22', winRate: 0.512, pickRate: 0.052, banRate: 0.098, games: 1350 },
]

describe('ChampionTrendChart', () => {
  it('formats every series as a whole percentage', async () => {
    const wrapper = await mountSuspended(ChampionTrendChart, {
      props: { points },
      global: { stubs: { ChampionTrendSeriesChart: true } },
    })

    const charts = wrapper.findAllComponents({ name: 'ChampionTrendSeriesChart' })
    expect(charts.map(chart => chart.props('title'))).toEqual(['Win rate', 'Pick rate', 'Ban rate'])
    for (const chart of charts) {
      const format = chart.props('format') as (value: number) => string
      expect(format(0.506)).toBe('51%')
      expect(format(0.0449)).toBe('4%')
    }
  })
})

describe('HomeDesktopBetaPanel', () => {
  it('titles the banner plainly and frames the capture without a shadow', async () => {
    const wrapper = await mountSuspended(HomeDesktopBetaPanel)

    expect(wrapper.find('h2').text()).toBe('Desktop app')
    expect(wrapper.find('a[href="/download"]').exists()).toBe(true)
    expect(wrapper.find('.ring-1 img').exists()).toBe(true)
    expect(wrapper.html()).not.toMatch(/\bshadow-\[|\bblur-3xl\b/)
  })
})
