import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, defineComponent, h, inject, provide, ref, watch } from 'vue'
import DraftLockButton from '../app/components/draft/DraftLockButton.vue'
import type { ChampSelectKind } from '~/composables/useChampSelectActions'
import { provideChampSelectActions } from '~/composables/useChampSelectActions'
import type { DraftState } from '~/types/lcu'

const ipc = vi.hoisted(() => ({ invoke: vi.fn() }))
vi.mock('@tauri-apps/api/core', () => ({ invoke: ipc.invoke }))

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

  it('locks through the shell, reads its choices, and says its refusal', async () => {
    ipc.invoke.mockReset()
    ipc.invoke.mockImplementation(async (name: string) => {
      if (name === 'champ_select_choices') return { writable: true, pickable: [105], bannable: [] }
      throw 'hover-mismatch'
    })
    const toasts: { title: string }[] = []
    vi.stubGlobal('useToast', () => ({ add: (toast: { title: string }) => toasts.push(toast) }))
    vi.stubGlobal('insideTauri', () => true)
    let api!: ReturnType<typeof provideChampSelectActions>
    mount(defineComponent({ setup: () => { api = provideChampSelectActions(ref(draft())); return () => h('div') } }))
    await api.lock()
    await new Promise(resolve => setTimeout(resolve, 0))
    expect(ipc.invoke).toHaveBeenCalledWith('champ_select_lock', { kind: 'pick', championId: 105 })
    expect(ipc.invoke).toHaveBeenCalledWith('champ_select_choices')
    expect(toasts.map(toast => toast.title)).toEqual(['Your hover changed'])
  })

  it('still says a lock', async () => {
    const { api, toasts } = setup()
    await api.lock()
    expect(toasts.map(toast => toast.title)).toEqual(['Fizz locked in'])
  })
})

describe('DraftSuggestions — the selected card', () => {
  const champions = new Map([[103, { id: 103, name: 'Ahri' }], [238, { id: 238, name: 'Zed' }], [61, { id: 61, name: 'Orianna' }]])

  async function strip(hovered: number | null, kind: ChampSelectKind | null = 'pick') {
    vi.stubGlobal('useChampionStatics', () => ({ nameOf: (id: number) => champions.get(id)?.name ?? '', champions: ref(champions) }))
    vi.stubGlobal('useChampSelectActions', () => ({ writable: ref(true), kind: ref(kind), hovered: ref(hovered) }))
    vi.stubGlobal('useDraftCardClick', () => ({ hovers: () => true, status: () => null, click: () => {} }))
    vi.stubGlobal('useTierList', () => ({ entryOf: () => null, status: ref('ready') }))
    vi.stubGlobal('useDamageProfiles', () => ({ profileOf: () => null }))
    vi.stubGlobal('usePlayerRecord', () => ({ games: ref([]) }))
    const { default: DraftSuggestions } = await import('../app/components/draft/DraftSuggestions.vue')
    return mount(DraftSuggestions, {
      props: {
        mode: 'pick',
        suggestions: [{ championId: 103, reasons: [], thin: false }, { championId: 238, reasons: [], thin: false }],
        hasPool: true,
        pending: false,
        position: 'MIDDLE',
        error: null,
        allies: [],
      },
      global: {
        stubs: {
          DraftSuggestionCard: {
            props: ['championId', 'focused'],
            template: '<div data-card :data-id="championId" :data-focused="focused"><slot /><slot name="action" /></div>',
          },
          DraftLockButton: { template: '<span data-lock />' },
          DraftChampionAction: true,
          ChampionPortrait: true,
          UTooltip: { template: '<div><slot /></div>' },
          UButton: true,
          UIcon: true,
          USwitch: true,
          UInput: true,
        },
      },
    })
  }

  it('rings the hovered champion\'s card and puts the lock under it alone', async () => {
    const wrapper = await strip(238)
    const cards = wrapper.findAll('[data-card]')
    // The best card stays in focus too: the hover rings the selected one without taking the focus off it.
    expect(cards[1]!.attributes('data-focused')).toBe('true')
    expect(cards[0]!.find('[data-lock]').exists()).toBe(false)
    expect(cards[1]!.find('[data-lock]').exists()).toBe(true)
  })

  it('lists a hover the podium does not rank beside it, with its lock', async () => {
    const wrapper = await strip(61)
    expect(wrapper.text()).toContain('Orianna')
    expect(wrapper.findAll('[data-card] [data-lock]')).toHaveLength(0)
    expect(wrapper.findAll('[data-lock]')).toHaveLength(1)
  })

  it('locks nothing out of our pick', async () => {
    const wrapper = await strip(238, 'ban')
    expect(wrapper.findAll('[data-lock]')).toHaveLength(0)
  })
})
