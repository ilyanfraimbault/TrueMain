import { mountSuspended } from '@nuxt/test-utils/runtime'
import { afterEach, describe, expect, it } from 'vitest'
import { flushPromises } from '@vue/test-utils'
import { AnalyticsOptOut } from '#components'
import PrivacyPage from '~/pages/privacy.vue'

// The privacy policy's opt-out switch (#2089): it reflects the flag the Umami
// tracker reads, and flipping it records or lifts the refusal.

afterEach(() => {
  window.localStorage.clear()
})

function readSwitch(wrapper: Awaited<ReturnType<typeof mountSuspended>>) {
  return wrapper.find('[role="switch"]')
}

describe('AnalyticsOptOut', () => {
  it('starts on, and records a refusal when switched off', async () => {
    const wrapper = await mountSuspended(AnalyticsOptOut)
    await flushPromises()

    expect(readSwitch(wrapper).attributes('aria-checked')).toBe('true')
    expect(wrapper.text()).toContain('Your visits are counted')

    await readSwitch(wrapper).trigger('click')
    await flushPromises()

    expect(window.localStorage.getItem('umami.disabled')).toBeTruthy()
    expect(readSwitch(wrapper).attributes('aria-checked')).toBe('false')
    expect(wrapper.text()).toContain('no longer measured')
  })

  it('reads an earlier refusal and lifts it when switched back on', async () => {
    window.localStorage.setItem('umami.disabled', '1')
    const wrapper = await mountSuspended(AnalyticsOptOut)
    await flushPromises()

    expect(readSwitch(wrapper).attributes('aria-checked')).toBe('false')

    await readSwitch(wrapper).trigger('click')
    await flushPromises()

    expect(window.localStorage.getItem('umami.disabled')).toBeNull()
    expect(readSwitch(wrapper).attributes('aria-checked')).toBe('true')
  })
})

describe('privacy page', () => {
  it('describes the audience measurement and carries the opt-out switch', async () => {
    const wrapper = await mountSuspended(PrivacyPage)
    await flushPromises()

    expect(wrapper.text()).toContain('We do not record sessions')
    // #2092: no referrer path, no campaign ids, 25-month retention.
    expect(wrapper.text()).toContain('never the address of the page itself')
    expect(wrapper.text()).toContain('utm_source')
    expect(wrapper.text()).toContain('deleted automatically 25 months after they are collected')
    expect(readSwitch(wrapper).exists()).toBe(true)
  })
})
