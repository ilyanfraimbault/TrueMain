import { flushPromises } from '@vue/test-utils'
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AppSearch, UApp } from '#components'

/**
 * The search palette tells its host when an exact Riot ID finds nobody (#2357):
 * once per opened palette, never for a plain name, so the count sizes an
 * "analyse this player" request rather than keystrokes.
 */
registerEndpoint('/api/truemains/search', () => ({ results: [], players: [] }))

afterEach(() => {
  document.body.innerHTML = ''
})

async function typeInPalette(value: string) {
  const input = await vi.waitFor(() => {
    const element = document.querySelector<HTMLInputElement>('input[type="text"], input:not([type])')
    expect(element).not.toBeNull()
    return element!
  })
  input.value = value
  input.dispatchEvent(new Event('input', { bubbles: true }))
  await new Promise(resolve => setTimeout(resolve, 400))
  await flushPromises()
}

async function openSearch() {
  const misses = vi.fn()
  useNuxtApp().hook('truemain:search-miss', misses)
  const wrapper = await mountSuspended(defineComponent({
    setup: () => () => h(UApp, null, () => h(AppSearch, { variant: 'field' })),
  }), { attachTo: document.body })
  await wrapper.get('button').trigger('click')
  await flushPromises()
  return misses
}

describe('search miss hook', () => {
  it('reports an exact Riot ID that finds nobody, once per opened palette', async () => {
    const misses = await openSearch()

    await typeInPalette('Nobody#EUW')
    await vi.waitFor(() => expect(misses).toHaveBeenCalledTimes(1))

    await typeInPalette('Nobody#EUW1')
    expect(misses).toHaveBeenCalledTimes(1)
  })

  it('stays quiet for a plain name', async () => {
    const misses = await openSearch()

    await typeInPalette('Nobody')
    expect(misses).not.toHaveBeenCalled()
  })
})
