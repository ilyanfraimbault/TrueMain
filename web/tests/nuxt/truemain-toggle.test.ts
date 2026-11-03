import { mountSuspended } from '@nuxt/test-utils/runtime'
import { afterEach, describe, expect, it } from 'vitest'
import { ChampionTruemainToggle, UApp } from '#components'

/**
 * The truemains toggle is shown only where the wider population is aggregated
 * (#2352): elsewhere its "every tracked player" side counts the same games.
 */
function mountToggle() {
  return mountSuspended(defineComponent({
    setup: () => () => h(UApp, null, () => h(ChampionTruemainToggle, { 'modelValue': true, 'onUpdate:modelValue': onUpdate })),
  }))
}

const updates: boolean[] = []
function onUpdate(value: boolean) {
  updates.push(value)
}

describe('truemains toggle', () => {
  afterEach(() => {
    useRuntimeConfig().public.populationToggle = false
    updates.length = 0
  })

  it('is hidden by default', async () => {
    const wrapper = await mountToggle()

    expect(wrapper.find('button').exists()).toBe(false)
  })

  it('shows and flips where the population toggle is on', async () => {
    useRuntimeConfig().public.populationToggle = true
    const wrapper = await mountToggle()

    const button = wrapper.get('button')
    expect(button.attributes('aria-pressed')).toBe('true')
    await button.trigger('click')
    expect(updates).toEqual([false])
  })
})
