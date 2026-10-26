import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { nextTick, shallowReactive } from 'vue'
import OpsCacheAgeNotice from '~~/app/components/OpsCacheAgeNotice.vue'
import { OPS_CACHE_LABEL_AFTER_MS } from '~/utils/ops-cache'

// The age label of the navigation cache (#2306): silent while what is on screen is
// recent, a minute-ladder label once a cached payload outlives the threshold, gone the
// moment its revalidation lands.

const NOW = 10_000_000
const global = { stubs: { UIcon: true } }

function mountWith(shown: Map<string, number>) {
  return mount(OpsCacheAgeNotice, { props: { shown, now: () => NOW }, global })
}

describe('OpsCacheAgeNotice', () => {
  it('renders nothing when no payload is drawn from the cache', () => {
    expect(mountWith(new Map()).find('output').exists()).toBe(false)
  })

  it('stays silent below the threshold', () => {
    const wrapper = mountWith(new Map([['k', NOW - OPS_CACHE_LABEL_AFTER_MS]]))
    expect(wrapper.find('output').exists()).toBe(false)
  })

  it('labels the oldest cached payload in minutes, then hours', () => {
    expect(mountWith(new Map([['a', NOW - 3 * 60_000], ['b', NOW - 90_000]])).text())
      .toBe('Showing figures cached 3 min ago')
    expect(mountWith(new Map([['a', NOW - 125 * 60_000]])).text())
      .toBe('Showing figures cached 2 h ago')
  })

  it('clears once the revalidation lands', async () => {
    const shown = shallowReactive(new Map([['a', NOW - 5 * 60_000]]))
    const wrapper = mountWith(shown)
    expect(wrapper.find('output').exists()).toBe(true)
    shown.delete('a')
    await nextTick()
    expect(wrapper.find('output').exists()).toBe(false)
  })
})
