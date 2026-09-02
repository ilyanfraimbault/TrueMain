import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { defineComponent, h } from 'vue'
import AreaChart from '~/components/charts/AreaChart.vue'
import { CHART_GUIDE_COLOR, CHART_PRIMARY } from '~/utils/chart-palette'

// <ChartsAreaChart> carries the decisions a call site would otherwise have to
// remember — legend, crosshair, escaping, the empty and loading states. All of
// them typecheck either way and none of them are visible in a build, so they are
// pinned here: the legend one in particular shipped broken and was caught only
// by opening a browser (an optional BOOLEAN prop cannot express "unset" — Vue
// casts an absent one to `false` — so a `??` fallback never ran).

const ONE_SERIES = { games: { name: 'Games', color: CHART_PRIMARY } }
const TWO_SERIES = {
  validated: { name: 'Validated', color: CHART_PRIMARY },
  demoted: { name: 'Demoted', color: '#38bdf8' },
}
const ROWS = [{ label: 'a', games: 1 }, { label: 'b', games: 2 }]

/** Records the props the wrapper hands to the upstream chart. */
const seen: Record<string, unknown>[] = []
const NcAreaChartStub = defineComponent({
  inheritAttrs: false,
  setup(_props, { attrs, slots }) {
    return () => {
      seen.push({ ...attrs })
      return h('div', { class: 'nc-area' }, slots.tooltip?.({ values: { label: 'a', games: 1 } }))
    }
  },
})

function mountChart(props: Record<string, unknown> = {}) {
  seen.length = 0
  return mount(AreaChart, {
    props: { data: ROWS, categories: ONE_SERIES, ...props },
    global: { stubs: { NcAreaChart: NcAreaChartStub, USkeleton: true, ClientOnly: { setup: (_p, { slots }) => () => slots.default?.() } } },
  })
}

/**
 * The stub declares no props, so every binding arrives as a fall-through attr
 * under the name the template wrote it with — `hide-legend`, not `hideLegend`.
 * Read them by either spelling so the assertions describe intent rather than
 * Vue's attr casing.
 */
function prop(name: string): unknown {
  const attrs = seen[seen.length - 1]!
  const kebab = name.replace(/[A-Z]/g, c => `-${c.toLowerCase()}`)
  return name in attrs ? attrs[name] : attrs[kebab]
}

describe('ChartsAreaChart series count', () => {
  it('hides the legend and keeps the accent crosshair on a single series', () => {
    mountChart()
    expect(prop('hideLegend')).toBe(true)
    expect((prop('crosshairConfig') as { color: string }).color).toBe(CHART_PRIMARY)
  })

  it('SHOWS the legend and goes neutral past one series', () => {
    // The regression this file exists for. Past one series, identity can no
    // longer be carried by colour alone, and an accent-coloured crosshair would
    // read as one more series.
    mountChart({ categories: TWO_SERIES })
    expect(prop('hideLegend')).toBe(false)
    expect((prop('crosshairConfig') as { color: string }).color).toBe(CHART_GUIDE_COLOR)
  })

  it('lets a call site override the legend through a fall-through attr', () => {
    // `$attrs` is bound after the derived props, which is what makes the
    // derivation a default rather than a rule.
    mountChart({ categories: TWO_SERIES, hideLegend: true })
    expect(prop('hideLegend')).toBe(true)
  })
})

describe('ChartsAreaChart empty and loading states', () => {
  it('renders the skeleton instead of the chart while loading', () => {
    const wrapper = mountChart({ loading: true })
    expect(wrapper.find('.nc-area').exists()).toBe(false)
    expect(wrapper.findComponent({ name: 'USkeleton' }).exists()).toBe(true)
  })

  it('renders the empty message instead of the chart when there is no row', () => {
    // `database.vue` drives this by passing `[]` rather than branching itself,
    // so an empty series must never reach the chart.
    const wrapper = mountChart({ data: [], emptyMessage: 'Not enough snapshots yet.' })
    expect(wrapper.find('.nc-area').exists()).toBe(false)
    expect(wrapper.text()).toContain('Not enough snapshots yet.')
  })

  it('prefers the skeleton over the empty state while a request is in flight', () => {
    // An empty array during a fetch means "not yet", not "nothing" — showing the
    // empty sentence there would call a loading panel a measurement.
    const wrapper = mountChart({ data: [], loading: true, emptyMessage: 'Nothing.' })
    expect(wrapper.text()).not.toContain('Nothing.')
  })

  it('draws the chart once rows arrive', () => {
    expect(mountChart().find('.nc-area').exists()).toBe(true)
  })
})

describe('ChartsAreaChart formatters and tooltip', () => {
  it('escapes tick text on the way to the axes (#842)', () => {
    mountChart({ xFormatter: () => 'Nunu & Willump' })
    expect((prop('xFormatter') as (t: number) => string)(0)).toBe('Nunu &amp; Willump')
  })

  it('leaves an absent formatter absent rather than wrapping undefined', () => {
    mountChart()
    expect(prop('xFormatter')).toBeUndefined()
  })

  it('renders its own tooltip when the call site gives no slot', () => {
    // The container is neutralised in `main.css`, so upstream's own tooltip
    // would draw bare text on nothing.
    expect(mountChart().text()).toContain('Games')
  })

  it('yields to the call site’s tooltip slot when there is one', () => {
    const wrapper = mount(AreaChart, {
      props: { data: ROWS, categories: ONE_SERIES },
      slots: { tooltip: '<p class="mine">custom</p>' },
      global: { stubs: { NcAreaChart: NcAreaChartStub, USkeleton: true, ClientOnly: { setup: (_p, { slots }) => () => slots.default?.() } } },
    })
    expect(wrapper.find('.mine').exists()).toBe(true)
    expect(wrapper.text()).not.toContain('Games')
  })

  it('fills a category that carries no colour from the palette, in slot order', () => {
    mountChart({ categories: { a: { name: 'A' }, b: { name: 'B' } } })
    const resolved = prop('categories') as Record<string, { color: string }>
    expect(resolved.a!.color).toBe(CHART_PRIMARY)
    expect(resolved.b!.color).not.toBe(CHART_PRIMARY)
  })
})
