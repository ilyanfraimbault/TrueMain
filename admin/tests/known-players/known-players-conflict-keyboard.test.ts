import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import KnownPlayersConflictActions from '~~/app/components/known-players/KnownPlayersConflictActions.vue'
import KnownPlayersConflictCompare from '~~/app/components/known-players/KnownPlayersConflictCompare.vue'
import KnownPlayersConflictList from '~~/app/components/known-players/KnownPlayersConflictList.vue'
import KnownPlayersConflicts from '~~/app/components/known-players/KnownPlayersConflicts.vue'
import KnownPlayersShortcutsHelp from '~~/app/components/known-players/KnownPlayersShortcutsHelp.vue'
import { conflict, persona } from './fixtures'

// The real `defineShortcuts` listens on window; the Nuxt UI components are stand-ins.
const stub = (tag: string, text?: (props: Record<string, unknown>) => string) => defineComponent({
  props: ['label', 'title', 'description', 'open', 'value'],
  setup(props, { slots, attrs }) {
    return () => h(tag, attrs, [text ? text(props) : '', slots.header?.(), slots.default?.(), slots.body?.()])
  },
})

const ui = {
  UBadge: stub('mark', p => String(p.label ?? '')),
  UButton: stub('button', p => String(p.label ?? '')),
  UCard: stub('section'),
  UInput: defineComponent({ setup: () => () => h('input', { 'data-search': '' }) }),
  USelect: stub('span'),
  UPagination: stub('nav'),
  USlideover: stub('div'),
  UModal: defineComponent({
    props: ['open', 'title', 'description'],
    setup: (props, { slots }) => () => props.open ? h('dialog', { 'data-help': '' }, slots.body?.()) : null,
  }),
  USeparator: stub('hr'),
  UIcon: stub('i'),
  UKbd: stub('kbd', p => String(p.value ?? '')),
  FetchErrorAlert: stub('aside', p => String(p.title ?? '')),
  KnownPlayersChannelConflicts: stub('div'),
  KnownPlayersBulkResolve: defineComponent({
    props: ['open'],
    setup: props => () => props.open ? h('dialog', { 'data-bulk': '' }) : null,
  }),
}

function press(key: string) {
  window.dispatchEvent(new KeyboardEvent('keydown', { key, shiftKey: key === '?' }))
}

function setup() {
  const data = ref({ conflicts: [conflict(), conflict({ id: 'c2' })], total: 2, page: 1, pageSize: 25, open: 2, resolved: 0 })
  const getPersonaConflict = vi.fn((id: string) => Promise.resolve({ conflict: conflict({ id }), claimant: persona({ id: 'p2' }), owner: persona() }))
  const resolvePersonaConflict = vi.fn((id: string) => {
    data.value = { ...data.value, conflicts: data.value.conflicts.filter(c => c.id !== id) }
    return Promise.resolve({ id, resolution: 'Kept', holderSlug: 'peng', mergedSlug: null, alsoResolved: 0 })
  })
  vi.stubGlobal('usePersonaConflicts', () => ({ data, pending: ref(false), error: ref(null), refresh: vi.fn() }))
  vi.stubGlobal('getPersonaConflict', getPersonaConflict)
  vi.stubGlobal('resolvePersonaConflict', resolvePersonaConflict)
  vi.stubGlobal('useActionToast', () => ({ success: vi.fn(), warning: vi.fn(), failure: vi.fn() }))
  vi.stubGlobal('useRoute', () => ({ query: { status: 'open' } }))
  vi.stubGlobal('useRouter', () => ({ replace: vi.fn() }))
  vi.stubGlobal('refDebounced', (source: unknown) => source)
  vi.stubGlobal('ALL', 'all')

  const wrapper = mount(KnownPlayersConflicts, {
    attachTo: document.body,
    props: { status: 'Open' },
    global: { components: { ...ui, KnownPlayersConflictList, KnownPlayersConflictActions, KnownPlayersConflictCompare, KnownPlayersShortcutsHelp } },
  })
  return { wrapper, getPersonaConflict, resolvePersonaConflict }
}

afterEach(() => {
  document.body.innerHTML = ''
  vi.unstubAllGlobals()
})

describe('KnownPlayersConflicts keyboard triage', () => {
  it('walks the queue, settles a conflict, opens the next and confirms an armed action with Enter', async () => {
    const { wrapper, getPersonaConflict, resolvePersonaConflict } = setup()
    await flushPromises()
    const selected = () => wrapper.findAll('[data-row]').findIndex(r => r.attributes('data-selected') !== undefined)

    press('j')
    press('j')
    await flushPromises()
    expect(selected()).toBe(1)
    press('k')
    await flushPromises()
    expect(selected()).toBe(0)

    press('Enter')
    await flushPromises()
    expect(getPersonaConflict).toHaveBeenCalledWith('c1')
    // The queue keys sleep while the compare is open.
    press('j')
    await flushPromises()
    expect(selected()).toBe(0)

    // Keep asks once: `h` sends it, and the next conflict opens on its own.
    press('h')
    await flushPromises()
    expect(resolvePersonaConflict).toHaveBeenCalledWith('c1', { action: 'keep' })
    expect(getPersonaConflict).toHaveBeenLastCalledWith('c2')

    // Merge asks twice: `1` arms the merge into the left column, Enter sends it.
    press('1')
    await flushPromises()
    expect(resolvePersonaConflict).toHaveBeenCalledTimes(1)
    expect(wrapper.find('[data-action="merge-claimant"]').text()).toContain('Confirm')
    press('Enter')
    await flushPromises()
    expect(resolvePersonaConflict).toHaveBeenLastCalledWith('c2', { action: 'merge', keep: 'claimant' })
    wrapper.unmount()
  })

  it('maps each compare key to its action and closes on Escape', async () => {
    const { wrapper, resolvePersonaConflict } = setup()
    await flushPromises()
    press('Enter')
    await flushPromises()
    for (const [key, action] of [['m', 'move'], ['d', 'detach'], ['2', 'merge-owner']] as const) {
      press(key)
      await flushPromises()
      expect(wrapper.find(`[data-action="${action}"]`).text()).toContain('Confirm')
    }
    expect(resolvePersonaConflict).not.toHaveBeenCalled()

    press('Escape')
    await flushPromises()
    press('m')
    await flushPromises()
    expect(wrapper.find('[data-action="move"]').text()).not.toContain('Confirm')
    expect(resolvePersonaConflict).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('shows the map on ? and ignores keys typed in a field', async () => {
    const { wrapper, getPersonaConflict } = setup()
    await flushPromises()
    press('?')
    await flushPromises()
    expect(wrapper.findAll('[data-help] [data-shortcut]').length).toBeGreaterThan(8)
    press('j')
    await flushPromises()
    expect(wrapper.find('[data-selected]').exists()).toBe(false)
    press('?')
    await flushPromises()
    expect(wrapper.find('[data-help]').exists()).toBe(false)

    wrapper.find<HTMLInputElement>('[data-search]').element.focus()
    press('j')
    press('Enter')
    await flushPromises()
    expect(wrapper.find('[data-selected]').exists()).toBe(false)
    expect(getPersonaConflict).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('keeps the queue keys quiet while the bulk resolution is open', async () => {
    const { wrapper, getPersonaConflict } = setup()
    await flushPromises()
    await wrapper.find('[data-action="bulk-resolve"]').trigger('click')
    expect(wrapper.find('[data-bulk]').exists()).toBe(true)
    press('j')
    press('Enter')
    await flushPromises()
    expect(wrapper.find('[data-selected]').exists()).toBe(false)
    expect(getPersonaConflict).not.toHaveBeenCalled()
    wrapper.unmount()
  })
})
