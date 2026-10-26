import { mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, nextTick, ref } from 'vue'
import AreaChart from '~/components/charts/AreaChart.vue'
import BarChart from '~/components/charts/BarChart.vue'
import DeployMarkers from '~/components/charts/DeployMarkers.vue'
import { useDeployMarkers, useDeployments } from '~/composables/useDeployMarkers'
import type { ChartMarker } from '~/utils/deploy-markers'
import { indexToPixelFromTicks, labelRows, placeDeployMarkers } from '~/utils/deploy-markers'
import type { Deployment } from '~~/shared/types/ops'

// Deploy markers (#2297). The placement is what makes a marker mean "this release started
// in this bucket", so it is pinned case by case; the overlay is pinned against a hand-built
// copy of the DOM unovis renders, since it reads the chart back rather than drawing into it.

const DAYS = ['2026-10-20T00:00:00Z', '2026-10-21T00:00:00Z', '2026-10-22T00:00:00Z', '2026-10-23T00:00:00Z']

function deploy(version: string, at: string, overrides: Partial<Deployment> = {}): Deployment {
  return { processName: 'Api', role: null, version, previousVersion: null, deployedAtUtc: at, ...overrides }
}

describe('placeDeployMarkers', () => {
  it('puts a deploy on the bucket that contains it', () => {
    const markers = placeDeployMarkers(DAYS, [deploy('1.2.0', '2026-10-21T14:30:00Z')])
    expect(markers).toEqual([{ index: 1, label: '1.2.0', title: 'Api 1.2.0 · 2026-10-21 14:30 UTC' }])
  })

  it('keeps a deploy on the last bucket, assumed as wide as the one before it', () => {
    expect(placeDeployMarkers(DAYS, [deploy('1.2.0', '2026-10-23T23:59:00Z')])[0]!.index).toBe(3)
  })

  it('does not draw a deploy outside the chart range', () => {
    const markers = placeDeployMarkers(DAYS, [
      deploy('1.1.0', '2026-10-19T23:59:00Z'),
      deploy('1.3.0', '2026-10-24T00:00:00Z'),
    ])
    expect(markers).toEqual([])
  })

  it('collapses several deploys of one bucket into one label', () => {
    const markers = placeDeployMarkers(DAYS, [
      deploy('1.3.0', '2026-10-22T18:00:00Z', { previousVersion: '1.2.0' }),
      deploy('1.2.0', '2026-10-22T09:00:00Z', { processName: 'Ingestor', role: 'FetchWorkers' }),
      deploy('1.3.0', '2026-10-22T18:01:00Z', { processName: 'Ingestor', role: 'FetchWorkers' }),
    ])
    expect(markers).toHaveLength(1)
    expect(markers[0]!.label).toBe('1.3.0 (+1)')
    expect(markers[0]!.title.split('\n')).toEqual([
      'Ingestor (FetchWorkers) 1.2.0 · 2026-10-22 09:00 UTC',
      'Api 1.3.0 from 1.2.0 · 2026-10-22 18:00 UTC',
      'Ingestor (FetchWorkers) 1.3.0 · 2026-10-22 18:01 UTC',
    ])
  })

  it('labels the Api and the ingestor rolling one release as that release alone', () => {
    const markers = placeDeployMarkers(DAYS, [
      deploy('1.2.0', '2026-10-21T10:00:00Z'),
      deploy('1.2.0', '2026-10-21T10:01:00Z', { processName: 'Ingestor', role: 'AggregateLane' }),
    ])
    expect(markers[0]!.label).toBe('1.2.0')
  })

  it('orders markers by row', () => {
    const markers = placeDeployMarkers(DAYS, [deploy('1.3.0', '2026-10-23T01:00:00Z'), deploy('1.2.0', '2026-10-20T01:00:00Z')])
    expect(markers.map(marker => marker.index)).toEqual([0, 3])
  })

  it('places nothing on a chart too short or unparseable to have a bucket width', () => {
    expect(placeDeployMarkers([DAYS[0]!], [deploy('1.2.0', DAYS[0]!)])).toEqual([])
    expect(placeDeployMarkers(['patch 25.20', 'patch 25.21'], [deploy('1.2.0', DAYS[0]!)])).toEqual([])
    expect(placeDeployMarkers(DAYS, [deploy('1.2.0', 'not a date')])).toEqual([])
  })
})

describe('indexToPixelFromTicks', () => {
  it('recovers the linear index scale from two ticks', () => {
    const toPixel = indexToPixelFromTicks([{ value: 0, position: 12 }, { value: 5, position: 112 }, { value: 10, position: 212 }])
    expect(toPixel?.(3)).toBe(72)
  })

  it('refuses to guess from fewer than two distinct ticks', () => {
    expect(indexToPixelFromTicks([{ value: 2, position: 40 }])).toBeNull()
    expect(indexToPixelFromTicks([{ value: 2, position: 40 }, { value: 2, position: 40 }])).toBeNull()
    expect(indexToPixelFromTicks([{ value: Number.NaN, position: 1 }, { value: 1, position: 2 }])).toBeNull()
  })
})

describe('labelRows', () => {
  it('alternates rows only where labels would collide', () => {
    expect(labelRows([10, 200, 230, 250, 600])).toEqual([0, 0, 1, 0, 0])
  })
})

describe('useDeployMarkers', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('reads one shared, day-stable window and places its deploys', () => {
    const calls: unknown[][] = []
    const data = ref<{ deployments: Deployment[] } | undefined>(undefined)
    vi.stubGlobal('useOps', (...args: unknown[]) => {
      calls.push(args)
      return { data }
    })

    const markers = useDeployMarkers(() => DAYS)
    expect(markers.value).toEqual([])

    data.value = { deployments: [deploy('1.2.0', '2026-10-22T05:00:00Z')] }
    expect(markers.value.map(marker => marker.index)).toEqual([2])

    useDeployments()
    expect(calls[0]![0]).toBe('/deployments')
    const from = (calls[0]![1] as { from: string }).from
    expect(from).toMatch(/T00:00:00\.000Z$/)
    expect(calls[1]).toEqual(calls[0])
  })
})

/** A host shaped like what unovis renders: an svg, an x-axis group, ticks carrying d3 data. */
function chartDom(ticks: { value: unknown, x: number }[]) {
  const svgNs = 'http://www.w3.org/2000/svg'
  const svg = document.createElementNS(svgNs, 'svg')
  const yAxis = document.createElementNS(svgNs, 'g')
  yAxis.setAttribute('class', 'css-1-axis-component')
  yAxis.setAttribute('transform', 'translate(40,0)')
  const yTick = document.createElementNS(svgNs, 'g')
  yTick.setAttribute('class', 'tick')
  yTick.setAttribute('transform', 'translate(0,100)')
  Object.assign(yTick, { __data__: 1000 })
  yAxis.appendChild(yTick)
  const xAxis = document.createElementNS(svgNs, 'g')
  xAxis.setAttribute('class', 'css-1-axis-component')
  xAxis.setAttribute('transform', 'translate(40,200)')
  for (const tick of ticks) {
    const g = document.createElementNS(svgNs, 'g')
    g.setAttribute('class', 'tick')
    g.setAttribute('transform', `translate(${tick.x},0)`)
    Object.assign(g, { __data__: tick.value })
    xAxis.appendChild(g)
  }
  svg.append(yAxis, xAxis)
  return svg
}

const frame = () => new Promise(resolve => setTimeout(resolve, 40))

function mountOverlay(markers: ChartMarker[], ticks: { value: unknown, x: number }[]) {
  const Host = defineComponent({
    setup() {
      return () => h('div', { class: 'host' }, [
        h('div', { ref: (el) => { if (el) (el as Element).appendChild(chartDom(ticks)) } }),
        h(DeployMarkers, { markers }),
      ])
    },
  })
  return mount(Host, { attachTo: document.body })
}

describe('DeployMarkers', () => {
  const markers: ChartMarker[] = [
    { index: 2, label: '1.2.0', title: 'Api 1.2.0' },
    { index: 3, label: '1.3.0', title: 'Api 1.3.0' },
  ]

  it('draws one rule per marker on the chart’s own index scale', async () => {
    const wrapper = mountOverlay(markers, [{ value: 0, x: 10 }, { value: 4, x: 410 }])
    await frame()
    await nextTick()

    const rules = wrapper.findAll('[data-testid="deploy-markers"] > div')
    expect(rules.map(rule => rule.attributes('style'))).toEqual([
      expect.stringContaining('left: 250px'),
      expect.stringContaining('left: 350px'),
    ])
    expect(rules[0]!.attributes('style')).toContain('height: 200px')
    const labels = wrapper.findAll('[data-testid="deploy-markers"] span')
    expect(labels.map(label => label.text())).toEqual(['1.2.0', '1.3.0'])
    expect(labels[0]!.attributes('title')).toBe('Api 1.2.0')
    wrapper.unmount()
  })

  it('draws nothing when the axis carries no readable ticks', async () => {
    const wrapper = mountOverlay(markers, [{ value: 'a', x: 10 }, { value: 'b', x: 410 }])
    await frame()
    expect(wrapper.findAll('[data-testid="deploy-markers"] > div')).toHaveLength(0)
    wrapper.unmount()
  })
})

const ClientOnly = { setup: (_p: unknown, { slots }: { slots: { default?: () => unknown } }) => () => slots.default?.() }

describe('chart wrappers', () => {
  const rows = [{ label: 'a', v: 1 }, { label: 'b', v: 2 }]
  const categories = { v: { name: 'V', color: '#fff' } }
  const marker: ChartMarker[] = [{ index: 1, label: '1.2.0', title: 'Api 1.2.0' }]

  it('adds the overlay to an area chart only when it has markers', () => {
    const stubs = { NcAreaChart: true, USkeleton: true, ClientOnly }
    const withMarkers = mount(AreaChart, { props: { data: rows, categories, markers: marker }, global: { stubs } })
    expect(withMarkers.findComponent(DeployMarkers).exists()).toBe(true)
    const without = mount(AreaChart, { props: { data: rows, categories }, global: { stubs } })
    expect(without.findComponent(DeployMarkers).exists()).toBe(false)
    const empty = mount(AreaChart, { props: { data: [], categories, markers: marker }, global: { stubs } })
    expect(empty.findComponent(DeployMarkers).exists()).toBe(false)
  })

  it('adds the overlay to a bar chart and keeps the prop off the upstream chart', () => {
    const seen: Record<string, unknown>[] = []
    const NcBarChart = defineComponent({
      inheritAttrs: false,
      setup(_p, { attrs }) {
        return () => {
          seen.push({ ...attrs })
          return h('div')
        }
      },
    })
    const wrapper = mount(BarChart, {
      props: { data: rows, height: 200, yAxis: ['v'], categories, markers: marker },
      global: { stubs: { NcBarChart } },
    })
    expect(wrapper.findComponent(DeployMarkers).exists()).toBe(true)
    expect(seen.at(-1)).not.toHaveProperty('markers')
  })
})
