import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, defineComponent, h, inject, provide, ref, watch } from 'vue'
import DraftLockButton from '../app/components/draft/DraftLockButton.vue'
import type { ChampSelectKind } from '~/composables/useChampSelectActions'
import { provideChampSelectActions } from '~/composables/useChampSelectActions'
import type { DraftState } from '~/types/lcu'

/**
 * Selecting and locking from the cards (#2465): a click hovers in silence, and
 * the hovered champion's card carries the "Lock in" button.
 */

const global = {
  stubs: {
    UTooltip: { props: ['text'], template: '<div :data-tooltip="text"><slot /></div>' },
    UPopover: {
      props: ['open'],
      emits: ['update:open'],
      template: '<div @click="$emit(\'update:open\', true)"><slot /><div v-if="open" data-popover><slot name="content" /></div></div>',
    },
    UButton: {
      props: ['label', 'disabled', 'ariaLabel'],
      emits: ['click'],
      template: '<button :disabled="disabled" :aria-label="ariaLabel" @click="$emit(\'click\')">{{ label }}</button>',
    },
  },
}

function actions(options: { kind?: ChampSelectKind | null, hovered?: number | null, canLock?: boolean, ally?: boolean } = {}) {
  const lock = vi.fn()
  vi.stubGlobal('useChampSelectActions', () => ({
    writable: ref(true),
    kind: ref(options.kind === undefined ? 'pick' : options.kind),
    hovered: ref(options.hovered === undefined ? 105 : options.hovered),
    canLock: ref(options.canLock ?? true),
    pending: ref(null),
    allyHovering: () => (options.ally ? { position: 'JUNGLE' } : null),
    allyName: () => 'your Jungle',
    lock,
  }))
  return { lock }
}

beforeEach(() => {
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('ref', ref)
  vi.stubGlobal('watch', watch)
  vi.stubGlobal('provide', provide)
  vi.stubGlobal('inject', inject)
  vi.stubGlobal('useChampionStatics', () => ({ nameOf: () => 'Fizz' }))
})

afterEach(() => vi.unstubAllGlobals())

describe('DraftLockButton', () => {
  it('locks the hovered pick on one click', async () => {
    const { lock } = actions()
    const wrapper = mount(DraftLockButton, { props: { block: true }, global })
    expect(wrapper.text()).toBe('Lock in')
    expect(wrapper.find('button').attributes('aria-label')).toBe('Lock in Fizz')
    await wrapper.find('button').trigger('click')
    expect(lock).toHaveBeenCalled()
  })

  it('asks first when the hover is an ally\'s pick intent', async () => {
    const { lock } = actions({ ally: true })
    const wrapper = mount(DraftLockButton, { global })
    await wrapper.find('button').trigger('click')
    expect(lock).not.toHaveBeenCalled()
    expect(wrapper.find('[data-popover]').text()).toContain('your Jungle\'s pick intent')
  })

  it('is disabled until our pick turn opens', () => {
    actions({ canLock: false })
    const wrapper = mount(DraftLockButton, { global })
    expect(wrapper.find('button').attributes('disabled')).toBeDefined()
    expect(wrapper.find('[data-tooltip]').attributes('data-tooltip')).toBe('Your pick turn has not started')
  })

  it('shows nothing with no hover, or out of a pick', () => {
    actions({ hovered: null })
    expect(mount(DraftLockButton, { global }).find('button').exists()).toBe(false)
    actions({ kind: 'ban' })
    expect(mount(DraftLockButton, { global }).find('button').exists()).toBe(false)
  })
})

describe('provideChampSelectActions', () => {
  function draft(): DraftState {
    return {
      myPosition: 'MIDDLE',
      myChampion: 105,
      myChampionLocked: false,
      allyChampions: [],
      enemyChampions: [],
      myTeam: [{ championId: 105, position: 'MIDDLE', locked: false, isMe: true }],
      allyBans: [],
      enemyBans: [],
      secondsLeft: 30,
      timerPhase: 'BAN_PICK',
      myAction: { id: 1, kind: 'pick', championId: 105, inProgress: true, completed: false },
      myNextAction: null,
      turnsUntilMyAction: null,
    }
  }

  function setup() {
    const toasts: { title: string }[] = []
    vi.stubGlobal('useToast', () => ({ add: (toast: { title: string }) => toasts.push(toast) }))
    vi.stubGlobal('insideTauri', () => false)
    let api!: ReturnType<typeof provideChampSelectActions>
    mount(defineComponent({ setup: () => { api = provideChampSelectActions(ref(draft())); return () => h('div') } }))
    return { api, toasts }
  }

  it('hovers without a toast', async () => {
    const { api, toasts } = setup()
    await api.hover(238)
    expect(toasts).toEqual([])
  })

  it('still says a lock', async () => {
    const { api, toasts } = setup()
    await api.lock()
    expect(toasts.map(toast => toast.title)).toEqual(['Fizz locked in'])
  })
})
