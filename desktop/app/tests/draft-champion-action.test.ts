import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import DraftChampionAction from '../app/components/draft/DraftChampionAction.vue'
import type { Availability, ChampSelectKind } from '~/composables/useChampSelectActions'

/**
 * The Hover / Ban control under a draft card (#1909, #2381): one click hovers,
 * a ban arms the same button and a second click writes it, and a disabled bar
 * says the champion's own reason — never a popover.
 */

const global = {
  stubs: {
    UTooltip: { props: ['text'], template: '<div :data-tooltip="text"><slot /></div>' },
    UButton: {
      props: ['label', 'disabled'],
      emits: ['click'],
      template: '<button :disabled="disabled" @click="$emit(\'click\')">{{ label }}</button>',
    },
  },
}

function actions(kind: ChampSelectKind | null, availability: Availability = { allowed: true, reason: null }, allyHovering = false) {
  const hover = vi.fn()
  const ban = vi.fn()
  const kindRef = ref(kind)
  vi.stubGlobal('useChampSelectActions', () => ({
    writable: ref(true),
    kind: kindRef,
    shownKind: computed(() => kindRef.value ?? 'pick'),
    pending: ref(null),
    availability: () => availability,
    allyHovering: () => (allyHovering ? { position: 'JUNGLE' } : null),
    allyName: () => 'your Jungle',
    hover,
    ban,
  }))
  return { hover, ban }
}

beforeEach(() => {
  vi.useFakeTimers()
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('ref', ref)
  vi.stubGlobal('watch', watch)
  vi.stubGlobal('onBeforeUnmount', onBeforeUnmount)
  vi.stubGlobal('useChampionStatics', () => ({ nameOf: () => 'Fizz' }))
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('DraftChampionAction', () => {
  it('hovers on one click on our pick', async () => {
    const { hover } = actions('pick')
    const wrapper = mount(DraftChampionAction, { props: { championId: 105, block: true }, global })
    expect(wrapper.text()).toBe('Hover')
    await wrapper.find('button').trigger('click')
    expect(hover).toHaveBeenCalledWith(105)
  })

  it('arms on a ban, writes on the second click', async () => {
    const { ban } = actions('ban')
    const wrapper = mount(DraftChampionAction, { props: { championId: 105, block: true }, global })
    await wrapper.find('button').trigger('click')
    expect(ban).not.toHaveBeenCalled()
    expect(wrapper.text()).toBe('Confirm')
    expect(wrapper.find('[data-tooltip]').attributes('data-tooltip')).toContain('cannot be undone')
    await wrapper.find('button').trigger('click')
    expect(ban).toHaveBeenCalledWith(105)
    expect(wrapper.text()).toBe('Ban')
  })

  it('disarms on its own when the second click does not come', async () => {
    const { ban } = actions('ban')
    const wrapper = mount(DraftChampionAction, { props: { championId: 105, block: true }, global })
    await wrapper.find('button').trigger('click')
    vi.advanceTimersByTime(4000)
    await wrapper.vm.$nextTick()
    expect(wrapper.text()).toBe('Ban')
    await wrapper.find('button').trigger('click')
    expect(ban).not.toHaveBeenCalled()
  })

  it('asks before hovering an ally\'s pick intent', async () => {
    const { hover } = actions('pick', { allowed: true, reason: null }, true)
    const wrapper = mount(DraftChampionAction, { props: { championId: 105 }, global })
    await wrapper.find('button').trigger('click')
    expect(hover).not.toHaveBeenCalled()
    expect(wrapper.find('[data-tooltip]').attributes('data-tooltip')).toContain('your Jungle\'s pick intent')
    await wrapper.find('button').trigger('click')
    expect(hover).toHaveBeenCalledWith(105)
  })

  it('writes the champion\'s own reason on a bar, keeps "not your turn" in the tooltip', () => {
    actions('ban', { allowed: false, reason: 'Already picked' })
    const picked = mount(DraftChampionAction, { props: { championId: 105, block: true }, global })
    expect(picked.text()).toBe('Already picked')
    expect(picked.find('button').attributes('disabled')).toBeDefined()

    actions(null, { allowed: false, reason: 'Not your turn — your pick in 2 turns' })
    const waiting = mount(DraftChampionAction, { props: { championId: 105, block: true }, global })
    expect(waiting.text()).toBe('Hover')
    expect(waiting.find('[data-tooltip]').attributes('data-tooltip')).toBe('Not your turn — your pick in 2 turns')
  })

  it('says a champion already hovered in the client', () => {
    actions('pick', { allowed: false, reason: 'Hovered in the client' })
    const wrapper = mount(DraftChampionAction, { props: { championId: 105, block: true }, global })
    expect(wrapper.text()).toBe('Hovered')
  })
})
