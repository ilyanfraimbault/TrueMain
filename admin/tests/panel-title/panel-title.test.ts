import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import PanelTitle from '~~/app/components/PanelTitle.vue'

// Pins the contract the "one line under a title" rule (#1414) rests on: the
// subtitle renders inline, the explanation only ever renders behind the info
// control, and that control is reachable by name. A regression here would put
// the prose back on the page — the exact thing the component exists to prevent.

// `UPopover` renders its `#content` in a portal in the real app; stubbing it as
// a passthrough is what lets the test assert on the info text at all.
const global = {
  stubs: {
    UPopover: {
      template: '<div class="popover"><slot /><slot name="content" /></div>',
    },
    UButton: {
      props: ['ariaLabel'],
      template: '<button type="button" :aria-label="ariaLabel" />',
    },
  },
}

describe('PanelTitle', () => {
  it('renders the title alone when nothing else is given', () => {
    const wrapper = mount(PanelTitle, { props: { title: 'Throughput' }, global })
    expect(wrapper.text()).toBe('Throughput')
    expect(wrapper.find('button').exists()).toBe(false)
  })

  it('renders a subtitle under the title', () => {
    const wrapper = mount(PanelTitle, {
      props: { title: 'Throughput', subtitle: 'By run date.' },
      global,
    })
    expect(wrapper.findAll('p').map(p => p.text())).toEqual(['Throughput', 'By run date.'])
  })

  it('puts the explanation behind a labelled info control', () => {
    const wrapper = mount(PanelTitle, {
      props: { title: 'Throughput', info: 'Bars are per-period flows.' },
      global,
    })
    const button = wrapper.find('button')
    expect(button.attributes('aria-label')).toBe('About Throughput')
    expect(wrapper.find('.popover').text()).toContain('Bars are per-period flows.')
  })

  it('lets the info slot replace the info prop', () => {
    const wrapper = mount(PanelTitle, {
      props: { title: 'Throughput', info: 'from the prop' },
      slots: { info: '<p>from the slot</p>' },
      global,
    })
    expect(wrapper.text()).toContain('from the slot')
    expect(wrapper.text()).not.toContain('from the prop')
  })

  it('renders a section label rather than a card title on the label variant', () => {
    const wrapper = mount(PanelTitle, {
      props: { title: 'Queue latency', variant: 'label' },
      global,
    })
    expect(wrapper.find('p').classes()).toContain('uppercase')
  })
})
