import { mount } from '@vue/test-utils'
import { Orientation } from '@unovis/ts'
import { describe, expect, it } from 'vitest'
import { h } from 'vue'
import BarChart from '~/components/charts/BarChart.vue'
import BarChartTooltip from '~/components/charts/BarChartTooltip.vue'

// <ChartsBarChart> exists to repair two vue-chrts tooltip defects, both of which
// typecheck, render, and are silently empty or silently WRONG in a browser. They
// are pinned here because nothing else would catch a regression: CI renders no
// charts, and a passing build says nothing about what the tooltip contains.

const CATEGORIES = {
  ladder: { name: 'Ladder', color: '#34d399' },
  harvest: { name: 'Harvest', color: '#fbbf24' },
}

function mountTooltip(values: unknown, props: Record<string, unknown> = {}) {
  return mount(BarChartTooltip, {
    props: { values, categories: CATEGORIES, ...props },
  })
}

describe('ChartsBarChartTooltip', () => {
  it('unwraps the datum @unovis/ts binds to a STACKED bar', () => {
    // The shape upstream's own tooltip fails on: the row is nested under
    // `datum`, so looking category keys up at the root finds nothing at all.
    const wrapper = mountTooltip({
      datum: { label: '2026-08-02', ladder: 110, harvest: 840 },
      index: 1,
      stacked: [0, 110],
      stackIndex: 0,
      isEnding: false,
    }, { titleFormatter: (row: { label: string }) => row.label })

    const text = wrapper.text()
    expect(text).toContain('2026-08-02')
    expect(text).toContain('Ladder')
    expect(text).toContain('110')
    expect(text).toContain('840')
    // The wrapper's own bookkeeping keys must never surface as series rows.
    expect(text).not.toContain('stackIndex')
  })

  it('takes a GROUPED/horizontal bar datum as the row itself', () => {
    const wrapper = mountTooltip(
      { label: '2026-08-02', ladder: 110, harvest: 840 },
      { titleFormatter: (row: { label: string }) => row.label },
    )
    expect(wrapper.text()).toContain('2026-08-02')
    expect(wrapper.text()).toContain('110')
  })

  it('formats values with yFormatter when the bars run vertically', () => {
    const wrapper = mountTooltip({ label: 'x', ladder: 1234, harvest: 0 }, {
      xFormatter: () => 'X-FORMATTER',
      yFormatter: (v: number) => `${v} rows`,
    })
    expect(wrapper.text()).toContain('1234 rows')
    expect(wrapper.text()).not.toContain('X-FORMATTER')
  })

  it('formats values with xFormatter when the bars run horizontally', () => {
    // @unovis/ts maps the VALUE to the bottom axis for horizontal bars, so a
    // horizontal chart's `yFormatter` is its index -> label lookup. Using it on
    // a value would print a category label where a count belongs.
    const wrapper = mountTooltip({ label: 'x', ladder: 1234, harvest: 0 }, {
      orientation: Orientation.Horizontal,
      xFormatter: (v: number) => `${v} games`,
      yFormatter: () => 'LABEL-LOOKUP',
    })
    expect(wrapper.text()).toContain('1234 games')
    expect(wrapper.text()).not.toContain('LABEL-LOOKUP')
  })

  it('drops categories the hovered datum carries no number for', () => {
    // `validated` is null for periods before the counter existed (#924); an
    // absent series must not render as a row reading "null".
    const wrapper = mountTooltip({ label: 'x', ladder: 5, harvest: null })
    expect(wrapper.text()).toContain('Ladder')
    expect(wrapper.text()).not.toContain('Harvest')
  })

  it('renders nothing before the first hover, when no datum exists yet', () => {
    expect(mountTooltip(undefined).text()).toBe('')
  })
})

describe('ChartsBarChart mousemove replay', () => {
  // Defect (2): the upstream trigger reads the tooltip markup out of the DOM one
  // frame before Vue has rendered it, so the first hover shows an empty box.
  // The wrapper replays one mousemove on the next frame to force a re-read.
  function mountChart() {
    return mount(BarChart, {
      props: { data: [{ ladder: 1 }], height: 100, yAxis: ['ladder'], categories: CATEGORIES },
      attachTo: document.body,
      global: {
        stubs: {
          NcBarChart: {
            setup: () => () => h('div', [
              h('span', { class: 'bar' }),
              h('span', { class: 'other-bar' }),
            ]),
          },
        },
      },
    })
  }
  const nextFrame = () => new Promise(resolve => requestAnimationFrame(() => setTimeout(resolve, 0)))

  it('replays one mousemove on the next frame', async () => {
    const wrapper = mountChart()
    const bar = wrapper.get('.bar').element
    let seen = 0
    bar.addEventListener('mousemove', () => { seen += 1 })

    bar.dispatchEvent(new MouseEvent('mousemove', { bubbles: true }))
    expect(seen).toBe(1) // the original only; the replay has not fired yet
    await nextFrame()
    expect(seen).toBe(2) // …and now it has
    wrapper.unmount()
  })

  it('replays the LATEST position, not the one that scheduled the frame', async () => {
    // A pointer crossing two bars inside one frame must be re-announced on the
    // bar it ended on. Replaying the first would paint that bar's values next to
    // a cursor that has already moved on — a wrong tooltip, worse than an empty
    // one.
    const wrapper = mountChart()
    const first = wrapper.get('.bar').element
    const second = wrapper.get('.other-bar').element
    const seen: string[] = []
    const record = (event: Event) => seen.push((event.target as Element).className)
    first.addEventListener('mousemove', record)
    second.addEventListener('mousemove', record)

    first.dispatchEvent(new MouseEvent('mousemove', { bubbles: true }))
    second.dispatchEvent(new MouseEvent('mousemove', { bubbles: true }))
    await nextFrame()

    // The two originals, then a single replay — on the bar the pointer ended on.
    expect(seen).toEqual(['bar', 'other-bar', 'other-bar'])
    wrapper.unmount()
  })

  it('drops the pending replay when the pointer leaves the chart', async () => {
    // Upstream hides the tooltip on mouseleave. A replay landing after that
    // would re-show it over a chart the pointer has left, with no further event
    // coming to hide it again — a tooltip stuck open.
    const wrapper = mountChart()
    const bar = wrapper.get('.bar').element
    let seen = 0
    bar.addEventListener('mousemove', () => { seen += 1 })

    bar.dispatchEvent(new MouseEvent('mousemove', { bubbles: true }))
    wrapper.element.dispatchEvent(new MouseEvent('mouseleave'))
    await nextFrame()

    expect(seen).toBe(1) // the original only — the replay was dropped
    wrapper.unmount()
  })

  it('does not replay the replay — one extra event, not a loop', async () => {
    const wrapper = mountChart()
    const bar = wrapper.get('.bar').element
    let seen = 0
    bar.addEventListener('mousemove', () => { seen += 1 })

    bar.dispatchEvent(new MouseEvent('mousemove', { bubbles: true }))
    await nextFrame()
    await nextFrame()
    await nextFrame()
    expect(seen).toBe(2)
    wrapper.unmount()
  })
})
