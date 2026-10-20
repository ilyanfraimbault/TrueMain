import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, nextTick, ref, watch } from 'vue'
import type { Chord, TriggerCheck } from '../app/types/overlay'
import RecordingsMarkKey from '../app/components/recordings/RecordingsMarkKey.vue'

// The setting outside Nuxt: its auto-imports stubbed as globals, the shell's
// check answered by the test, its Nuxt UI children as plain elements.
const checkMarkKey = vi.fn<(chord: Chord) => Promise<TriggerCheck>>()

const global = {
  stubs: {
    USwitch: {
      props: ['modelValue', 'label', 'description'],
      emits: ['update:modelValue'],
      template: '<button class="switch" :data-on="modelValue" @click="$emit(\'update:modelValue\', !modelValue)">{{ description }}</button>',
    },
    UIcon: true,
  },
}

const ALT_SHIFT_0: Chord = { alt: true, shift: true, ctrl: false, meta: false, tab: false, key: 'Digit0' }

beforeEach(() => {
  vi.stubGlobal('ref', ref)
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('watch', watch)
  vi.stubGlobal('nextTick', nextTick)
  vi.stubGlobal('useRecordings', () => ({ checkMarkKey }))
  checkMarkKey.mockReset()
  checkMarkKey.mockImplementation(async () => ({ refusal: null, chord: { label: '⌥⇧M', warning: null } }))
})

afterEach(() => vi.unstubAllGlobals())

describe('RecordingsMarkKey', () => {
  it('shows the key and what it keeps', () => {
    const wrapper = mount(RecordingsMarkKey, { global, props: { markKey: ALT_SHIFT_0, chord: { label: '⌥⇧0', warning: null } } })

    expect(wrapper.find('.switch').attributes('data-on')).toBe('true')
    expect(wrapper.find('.switch').text()).toContain('the 30 seconds before')
    expect(wrapper.find('button[aria-label^="Mark key"]').text()).toBe('⌥⇧0')
  })

  it('records a chord from the keys pressed and saves it once the shell accepts it', async () => {
    const wrapper = mount(RecordingsMarkKey, { global, props: { markKey: ALT_SHIFT_0, chord: { label: '⌥⇧0', warning: null } } })
    const field = wrapper.find('button[aria-label^="Mark key"]')

    await field.trigger('click')
    expect(field.text()).toBe('Press the keys…')
    await field.trigger('keydown', { code: 'AltLeft', altKey: true })
    await field.trigger('keydown', { code: 'KeyM', altKey: true, shiftKey: true })
    await flushPromises()

    const chord = { alt: true, shift: true, ctrl: false, meta: false, tab: false, key: 'KeyM' }
    expect(checkMarkKey).toHaveBeenCalledWith(chord)
    expect(wrapper.emitted('change')).toEqual([[chord]])
  })

  it('shows the shell\'s refusal and saves nothing', async () => {
    checkMarkKey.mockImplementation(async () => ({ refusal: 'Already the shortcut of Next item.', chord: { label: '⌥⇧1', warning: null } }))
    const wrapper = mount(RecordingsMarkKey, { global, props: { markKey: ALT_SHIFT_0, chord: { label: '⌥⇧0', warning: null } } })
    const field = wrapper.find('button[aria-label^="Mark key"]')

    await field.trigger('click')
    await field.trigger('keydown', { code: 'Digit1', altKey: true, shiftKey: true })
    await flushPromises()

    expect(wrapper.emitted('change')).toBeUndefined()
    expect(wrapper.find('.text-error').text()).toBe('Already the shortcut of Next item.')
    expect(field.text()).toBe('⌥⇧1')
  })

  it('refuses a key no chord can use without asking the shell', async () => {
    const wrapper = mount(RecordingsMarkKey, { global, props: { markKey: ALT_SHIFT_0, chord: null } })
    const field = wrapper.find('button[aria-label^="Mark key"]')

    await field.trigger('click')
    await field.trigger('keydown', { code: 'NumpadEnter' })

    expect(checkMarkKey).not.toHaveBeenCalled()
    expect(wrapper.find('.text-error').text()).toBe('This key cannot be part of a shortcut.')
  })

  it('warns about a key the game also uses', () => {
    const warning = 'Self-cast your Q ability in game: the shortcut will also self-cast your Q ability.'
    const wrapper = mount(RecordingsMarkKey, { global, props: { markKey: { ...ALT_SHIFT_0, shift: false, key: 'KeyQ' }, chord: { label: '⌥Q', warning } } })

    expect(wrapper.find('.text-warning').text()).toBe(warning)
  })

  it('turns marking off, and back on at the default key', async () => {
    const on = mount(RecordingsMarkKey, { global, props: { markKey: ALT_SHIFT_0, chord: { label: '⌥⇧0', warning: null } } })
    await on.find('.switch').trigger('click')
    expect(on.emitted('change')).toEqual([[null]])

    const off = mount(RecordingsMarkKey, { global, props: { markKey: null, chord: null } })
    expect(off.find('button[aria-label^="Mark key"]').exists()).toBe(false)
    await off.find('.switch').trigger('click')
    await flushPromises()
    expect(checkMarkKey).toHaveBeenCalledWith(ALT_SHIFT_0)
    expect(off.emitted('change')).toEqual([[ALT_SHIFT_0]])
  })
})
