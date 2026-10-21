import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed } from 'vue'
import type { AutoClipKind } from '../app/types/recordings'
import RecordingsAutoClips from '../app/components/recordings/RecordingsAutoClips.vue'
import { AUTO_CLIP_KINDS, clipOrigin, toggleAutoClip } from '../app/utils/auto-clips'

// The setting outside Nuxt: its auto-imports stubbed as globals, its Nuxt UI
// children as plain elements.
const global = {
  stubs: {
    USwitch: {
      props: ['modelValue', 'label', 'description'],
      emits: ['update:modelValue'],
      template: '<button class="switch" :data-on="modelValue" @click="$emit(\'update:modelValue\', !modelValue)">{{ label }}</button>',
    },
    UCheckbox: {
      props: ['modelValue', 'label', 'description'],
      emits: ['update:modelValue'],
      template: '<button class="kind" :data-on="modelValue" @click="$emit(\'update:modelValue\', !modelValue)">{{ label }}</button>',
    },
  },
}

const ALL: AutoClipKind[] = ['multiKill', 'steal', 'ace', 'firstBlood', 'teamfight', 'combo']

beforeEach(() => vi.stubGlobal('computed', computed))
afterEach(() => vi.unstubAllGlobals())

describe('RecordingsAutoClips', () => {
  it('lists every play the shell can clip, each ticked when chosen', () => {
    const wrapper = mount(RecordingsAutoClips, { global, props: { kinds: ['steal', 'combo'] } })

    expect(wrapper.find('.switch').attributes('data-on')).toBe('true')
    const kinds = wrapper.findAll('.kind')
    expect(kinds.map(kind => kind.text())).toEqual(AUTO_CLIP_KINDS.map(kind => kind.label))
    expect(kinds.filter(kind => kind.attributes('data-on') === 'true').map(kind => kind.text())).toEqual(['Kills into an objective', 'Steals'])
  })

  it('turns a play on or off, in the shell\'s order', async () => {
    const wrapper = mount(RecordingsAutoClips, { global, props: { kinds: ['steal', 'combo'] } })
    const kinds = wrapper.findAll('.kind')

    await kinds.find(kind => kind.text() === 'Multi-kills')!.trigger('click')
    await kinds.find(kind => kind.text() === 'Steals')!.trigger('click')

    expect(wrapper.emitted('change')).toEqual([[['multiKill', 'steal', 'combo']], [['combo']]])
  })

  it('turns every play off, and back on all at once', async () => {
    const wrapper = mount(RecordingsAutoClips, { global, props: { kinds: ALL } })
    await wrapper.find('.switch').trigger('click')
    expect(wrapper.emitted('change')).toEqual([[[]]])

    await wrapper.setProps({ kinds: [] })
    expect(wrapper.findAll('.kind')).toHaveLength(0)
    await wrapper.find('.switch').trigger('click')
    expect(wrapper.emitted('change')![1]![0]).toEqual(expect.arrayContaining(ALL))
  })
})

describe('auto clips', () => {
  it('keeps the shell\'s order whatever the order of the clicks', () => {
    expect(toggleAutoClip(['combo'], 'multiKill', true)).toEqual(['multiKill', 'combo'])
    expect(toggleAutoClip(ALL, 'ace', false)).toEqual(['multiKill', 'steal', 'firstBlood', 'teamfight', 'combo'])
    expect(toggleAutoClip([], 'ace', false)).toEqual([])
  })

  it('says where a clip came from', () => {
    const marked = { kind: 'marked' as const, videoMs: 30_000, endVideoMs: 30_000, kills: 0, ally: null, killerId: null, victimIds: [], dragon: null }
    expect(clipOrigin({ moments: [marked], automatic: [] }).label).toBe('Marked in game')
    expect(clipOrigin({ moments: [], automatic: ['steal'] })).toEqual({ icon: 'i-lucide-sparkles', label: 'Clipped automatically' })
    expect(clipOrigin({ moments: [], automatic: [] }).icon).toBe('i-lucide-scissors')
  })
})
