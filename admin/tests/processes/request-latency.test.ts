import type { RequestLatency, RouteLatency, RouteLatencyPoint } from '~~/shared/types/ops'
import { mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import ProcessesLatencySparkline from '~~/app/components/processes/ProcessesLatencySparkline.vue'
import ProcessesPerformance from '~~/app/components/processes/ProcessesPerformance.vue'
import ProcessesPerformanceTable from '~~/app/components/processes/ProcessesPerformanceTable.vue'
import { countSlowRoutes, formatLatency, isSlowRoute, sparkline } from '~~/shared/utils/request-latency'

// Pins the Performance tab (#2307): how a duration reads, which routes are slow, that an idle
// bin breaks the sparkline instead of reading as fast, and that the table keeps the API's
// slowest-first order with the slow routes marked.

function point(p95Ms: number | null, index = 0): RouteLatencyPoint {
  return { binStartUtc: new Date(Date.UTC(2026, 9, 26, index)).toISOString(), count: p95Ms === null ? 0 : 3, p95Ms }
}

function route(overrides: Partial<RouteLatency>): RouteLatency {
  return {
    route: '/ops/logs',
    method: 'GET',
    count: 10,
    p50Ms: 40,
    p95Ms: 90,
    meanMs: 50,
    maxMs: 120,
    timeouts: 0,
    serverErrors: 0,
    series: [point(80, 0), point(null, 1), point(90, 2)],
    ...overrides,
  }
}

const latency: RequestLatency = {
  window: '24h',
  sinceUtc: '2026-10-25T12:00:00Z',
  generatedAtUtc: '2026-10-26T12:00:00Z',
  binHours: 1,
  slowThresholdMs: 1000,
  retentionDays: 30,
  oldestRetainedUtc: '2026-10-20T00:00:00Z',
  droppedSinceStart: 0,
  routes: [
    route({ route: '/ops/stats/overview', p50Ms: 2_400, p95Ms: 6_200, maxMs: 7_000, timeouts: 2, serverErrors: 1 }),
    route({}),
  ],
}

describe('request-latency utils', () => {
  it('reads milliseconds under a second and seconds above', () => {
    expect(formatLatency(42.4)).toBe('42 ms')
    expect(formatLatency(1_250)).toBe('1.25 s')
    expect(formatLatency(12_340)).toBe('12.3 s')
    expect(formatLatency(null)).toBe('—')
    expect(formatLatency(Number.NaN)).toBe('—')
  })

  it('calls a route slow only above the threshold', () => {
    expect(isSlowRoute({ p95Ms: 1_000 }, 1_000)).toBe(false)
    expect(isSlowRoute({ p95Ms: 1_001 }, 1_000)).toBe(true)
    expect(countSlowRoutes(latency.routes, 1_000)).toBe(1)
  })

  it('breaks the line on an idle bin and keeps a lone bin as a point', () => {
    const geometry = sparkline([point(100, 0), point(200, 1), point(null, 2), point(400, 3)], 30, 10, 1_000)

    expect(geometry.segments).toHaveLength(2)
    expect(geometry.segments[0]).toHaveLength(2)
    expect(geometry.segments[1]).toEqual([{ x: 30, y: 6 }])
    // The scale tops out at the threshold, so the threshold line sits on the top edge.
    expect(geometry.thresholdY).toBe(0)
  })

  it('scales to the slowest bin when it is above the threshold', () => {
    const geometry = sparkline([point(500, 0), point(2_000, 1)], 10, 20, 1_000)

    expect(geometry.segments[0]![1]).toEqual({ x: 10, y: 0 })
    expect(geometry.thresholdY).toBe(10)
  })

  it('centres a single-bin series', () => {
    expect(sparkline([point(500)], 40, 10, 1_000).segments).toEqual([[{ x: 20, y: 5 }]])
  })
})

describe('ProcessesLatencySparkline', () => {
  it('draws one polyline per run and a dot per lone bin', () => {
    const wrapper = mount(ProcessesLatencySparkline, {
      props: { series: [point(100, 0), point(200, 1), point(null, 2), point(400, 3)], thresholdMs: 1_000, slow: true },
    })

    expect(wrapper.findAll('polyline')).toHaveLength(1)
    expect(wrapper.findAll('circle')).toHaveLength(1)
    expect(wrapper.find('svg').classes()).toContain('text-error')
  })
})

describe('ProcessesPerformanceTable', () => {
  const global = {
    stubs: {
      UBadge: { props: ['label'], template: '<span class="badge">{{ label }}</span>' },
    },
    components: { ProcessesLatencySparkline },
  }

  it('keeps the slowest-first order and marks the routes over the threshold', () => {
    const wrapper = mount(ProcessesPerformanceTable, { props: { latency }, global })
    const rows = wrapper.findAll('tbody tr')

    expect(rows).toHaveLength(2)
    expect(rows[0]!.text()).toContain('/ops/stats/overview')
    expect(rows[0]!.text()).toContain('6.20 s')
    expect(rows[0]!.attributes('data-slow')).toBe('true')
    expect(rows[0]!.find('.badge').text()).toBe('slow')
    expect(rows[1]!.text()).toContain('/ops/logs')
    expect(rows[1]!.text()).toContain('90 ms')
    expect(rows[1]!.attributes('data-slow')).toBeUndefined()
    expect(rows[1]!.find('.badge').exists()).toBe(false)
    expect(wrapper.findAll('svg')).toHaveLength(2)
  })
})

describe('ProcessesPerformance', () => {
  const stub = (tag: string, text?: (props: Record<string, unknown>) => string) => defineComponent({
    props: ['label', 'title', 'subtitle'],
    setup(props, { slots }) {
      return () => h(tag, [text ? text(props) : '', slots.header?.(), slots.default?.()])
    },
  })
  const components = {
    UBadge: stub('mark', p => String(p.label ?? '')),
    UCard: stub('section'),
    USelect: stub('span'),
    USkeleton: stub('div', () => 'loading'),
    PanelTitle: stub('h2', p => String(p.title ?? '')),
    FetchErrorAlert: stub('aside', p => String(p.title ?? '')),
    ProcessesPerformanceTable,
    ProcessesLatencySparkline,
  }

  function stubFetch(data: unknown, options: { pending?: boolean, error?: unknown } = {}) {
    const refresh = vi.fn()
    const window = vi.fn()
    vi.stubGlobal('useRequestLatency', (selected: unknown) => {
      window(selected)
      return { data: ref(data), pending: ref(options.pending ?? false), refreshing: ref(options.pending ?? false), error: ref(options.error ?? null), refresh }
    })
    vi.stubGlobal('useLastGoodPayload', (payload: unknown) => payload)
    return { refresh, window }
  }

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('counts the slow routes and draws the table', () => {
    const { refresh } = stubFetch(latency)
    const wrapper = mount(ProcessesPerformance, { global: { components } })

    expect(wrapper.text()).toContain('1 route with p95 over 1.00 s')
    expect(wrapper.text()).toContain('Ops request latency')
    expect(wrapper.findAll('tbody tr')).toHaveLength(2)
    ;(wrapper.vm as unknown as { refresh: () => void }).refresh()
    expect(refresh).toHaveBeenCalled()
  })

  it('says when nothing was ever measured, and when part of the window was', () => {
    stubFetch({ ...latency, oldestRetainedUtc: null, routes: [] })
    expect(mount(ProcessesPerformance, { global: { components } }).text())
      .toContain('Nothing measured yet')

    stubFetch({ ...latency, oldestRetainedUtc: '2026-10-26T00:00:00Z', routes: [], droppedSinceStart: 12 })
    const partial = mount(ProcessesPerformance, { global: { components } }).text()
    expect(partial).toContain('Measured since')
    expect(partial).toContain('No ops route was called in this window.')
    expect(partial).toContain('12 requests were measured but not persisted')
  })

  it('shows the skeleton while the first reading loads, and the error with the last reading', () => {
    stubFetch(null, { pending: true })
    expect(mount(ProcessesPerformance, { global: { components } }).text()).toContain('loading')

    stubFetch(latency, { error: new Error('boom') })
    const failed = mount(ProcessesPerformance, { global: { components } }).text()
    expect(failed).toContain('Failed to load ops request latency')
    expect(failed).toContain('Showing the last successful reading')
  })
})
