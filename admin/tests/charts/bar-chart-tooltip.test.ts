import { mount } from '@vue/test-utils'
import { Orientation } from '@unovis/ts'
import { describe, expect, it } from 'vitest'
import { h } from 'vue'
import BarChart from '~/components/charts/BarChart.vue'

// <ChartsBarChart> exists to repair two vue-chrts tooltip defects. Both are
// invisible to a typecheck and to any test that does not render the tooltip
// slot, and both produced a silently empty or silently WRONG tooltip in the
// browser, so they are pinned here.
//
// The stub stands in for <NcBarChart>: it renders the `tooltip` slot with a
// datum the way @unovis/ts binds one, which is the whole point of the exercise.
function mountWithDatum(values: unknown, props: Record<string, unknown> = {}) {
  return mount(BarChart, {
    props: {
      categories: {
        ladder: { name: 'Ladder', color: '#34d399' },
        harvest: { name: 'Harvest', color: '#fbbf24' },
      },
      ...props,
    },
    global: {
      stubs: {
        NcBarChart: {
          setup(_: unknown, { slots }: { slots: Record<string, ((p: unknown) => unknown) | undefined> }) {
            return () => h('div', slots.tooltip?.({ values }) as never)
          },
        },
      },
    },
  })
}

describe('ChartsBarChart tooltip', () => {
  it('unwraps the datum @unovis/ts binds to a STACKED bar', () => {
    // The shape upstream's own tooltip fails on: the row is nested under
    // `datum`, so looking category keys up at the root finds nothing.
    const wrapper = mountWithDatum({
      datum: { label: '2026-08-02', ladder: 110, harvest: 840 },
      index: 1,
      stacked: [0, 110],
      stackIndex: 0,
      isEnding: false,
    }, { tooltipTitleFormatter: (row: { label: string }) => row.label })

    const text = wrapper.text()
    expect(text).toContain('2026-08-02')
    expect(text).toContain('Ladder')
    expect(text).toContain('110')
    expect(text).toContain('840')
    // The wrapper's own bookkeeping keys must never surface as series rows.
    expect(text).not.toContain('stackIndex')
  })

  it('takes a GROUPED/horizontal bar datum as the row itself', () => {
    const wrapper = mountWithDatum(
      { label: '2026-08-02', ladder: 110, harvest: 840 },
      { tooltipTitleFormatter: (row: { label: string }) => row.label },
    )
    expect(wrapper.text()).toContain('2026-08-02')
    expect(wrapper.text()).toContain('110')
  })

  it('formats values with yFormatter when the bars run vertically', () => {
    const wrapper = mountWithDatum({ label: 'x', ladder: 1234, harvest: 0 }, {
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
    const wrapper = mountWithDatum({ label: 'x', ladder: 1234, harvest: 0 }, {
      orientation: Orientation.Horizontal,
      xFormatter: (v: number) => `${v} games`,
      yFormatter: () => 'LABEL-LOOKUP',
    })
    expect(wrapper.text()).toContain('1234 games')
    expect(wrapper.text()).not.toContain('LABEL-LOOKUP')
  })

  it('drops categories the hovered datum carries no number for', () => {
    // `validated` is null for periods before the counter existed (#924); an
    // absent series must not render as a row saying "null".
    const wrapper = mountWithDatum({ label: 'x', ladder: 5, harvest: null })
    expect(wrapper.text()).toContain('Ladder')
    expect(wrapper.text()).not.toContain('Harvest')
  })

  it('renders nothing before the first hover, when no datum exists yet', () => {
    expect(mountWithDatum(undefined).text()).toBe('')
  })
})
