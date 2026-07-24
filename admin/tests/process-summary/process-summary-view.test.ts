import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ProcessSummaryView from '~~/app/components/ProcessSummaryView.vue'

// Pins the shape -> render mapping of the recursive summary renderer: which of
// the five layouts (scalar / field list / table / chips / indexed list) a given
// free-form payload lands on, plus the depth-cap raw-JSON fallback. These are
// the paths that would silently change if the `summary` schema evolves.
function mountView(value: unknown, depth?: number) {
  return mount(ProcessSummaryView, { props: { value, depth } })
}

describe('ProcessSummaryView — scalar', () => {
  it('renders a scalar as a single formatted line', () => {
    const wrapper = mountView(1234)
    expect(wrapper.find('span').text()).toBe('1,234')
    expect(wrapper.find('dl').exists()).toBe(false)
    expect(wrapper.find('table').exists()).toBe(false)
  })

  it('renders null as an em dash', () => {
    expect(mountView(null).find('span').text()).toBe('—')
  })

  it('renders booleans as Yes/No', () => {
    expect(mountView(true).find('span').text()).toBe('Yes')
  })
})

describe('ProcessSummaryView — object', () => {
  it('renders a flat object as a label/value field list', () => {
    const wrapper = mountView({ matchesIngested: 1200, platform: 'EUW1', failed: false })
    expect(wrapper.findAll('dt').map(dt => dt.text()))
      .toEqual(['Matches Ingested', 'Platform', 'Failed'])
    expect(wrapper.findAll('dd').map(dd => dd.text()))
      .toEqual(['1,200', 'EUW1', 'No'])
  })

  it('recurses into a nested object as a sub-group', () => {
    const wrapper = mountView({ totals: { ingested: 5 } })
    const nested = wrapper.find('dd dl')
    expect(nested.exists()).toBe(true)
    expect(wrapper.findAll('dt').map(dt => dt.text())).toEqual(['Totals', 'Ingested'])
    expect(nested.find('dd').text()).toBe('5')
  })

  it('renders an empty object as "(empty)"', () => {
    const wrapper = mountView({})
    expect(wrapper.text()).toBe('(empty)')
    expect(wrapper.find('dl').exists()).toBe(false)
  })
})

describe('ProcessSummaryView — array of objects', () => {
  it('renders a table whose columns are the union of the row keys', () => {
    const wrapper = mountView([
      { platform: 'EUW1', ingested: 1200 },
      { platform: 'KR', skipped: 3 },
    ])
    expect(wrapper.findAll('th').map(th => th.text()))
      .toEqual(['Platform', 'Ingested', 'Skipped'])
    const rows = wrapper.findAll('tbody tr')
    expect(rows).toHaveLength(2)
    expect(rows[0]!.findAll('td').map(td => td.text())).toEqual(['EUW1', '1,200', '—'])
    expect(rows[1]!.findAll('td').map(td => td.text())).toEqual(['KR', '—', '3'])
  })

  it('recurses inside a cell holding a nested structure', () => {
    const wrapper = mountView([{ platform: 'EUW1', totals: { ingested: 5 } }])
    expect(wrapper.find('td dl').exists()).toBe(true)
    expect(wrapper.find('td dl dt').text()).toBe('Ingested')
  })
})

describe('ProcessSummaryView — array of scalars', () => {
  it('renders chips, not a table', () => {
    const wrapper = mountView(['EUW1', 'KR', 12])
    expect(wrapper.find('table').exists()).toBe(false)
    expect(wrapper.findAll('span').map(span => span.text())).toEqual(['EUW1', 'KR', '12'])
  })

  it('renders an empty array as "(empty)"', () => {
    const wrapper = mountView([])
    expect(wrapper.text()).toBe('(empty)')
    expect(wrapper.find('table').exists()).toBe(false)
  })
})

describe('ProcessSummaryView — mixed array', () => {
  it('renders an indexed list, recursing per item', () => {
    const wrapper = mountView(['EUW1', { platform: 'KR', ingested: 3 }])
    expect(wrapper.find('table').exists()).toBe(false)
    expect(wrapper.text()).toContain('#1')
    expect(wrapper.text()).toContain('#2')
    // The object item still renders as a field list inside its entry.
    expect(wrapper.find('dl').exists()).toBe(true)
    expect(wrapper.find('dt').text()).toBe('Platform')
  })
})

describe('ProcessSummaryView — depth cap', () => {
  /** Nest `{ child: … }` `levels` deep around a scalar leaf. */
  function nest(levels: number): unknown {
    let value: unknown = 'leaf'
    for (let i = 0; i < levels; i++) {
      value = { child: value }
    }
    return value
  }

  it('keeps recursing below the cap', () => {
    const wrapper = mountView(nest(3))
    expect(wrapper.find('pre').exists()).toBe(false)
    expect(wrapper.findAll('dl')).toHaveLength(3)
  })

  it('falls back to raw JSON once the cap is reached by recursion', () => {
    const wrapper = mountView(nest(10))
    const raw = wrapper.find('pre')
    expect(raw.exists()).toBe(true)
    expect(JSON.parse(raw.text())).toEqual({ child: { child: 'leaf' } })
  })

  it('falls back to raw JSON immediately when mounted at the cap', () => {
    const wrapper = mountView({ platform: 'EUW1' }, 8)
    expect(wrapper.find('dl').exists()).toBe(false)
    expect(wrapper.find('pre').text()).toBe(JSON.stringify({ platform: 'EUW1' }, null, 2))
  })
})
