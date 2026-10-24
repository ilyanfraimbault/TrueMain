import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import KnownPlayersConflictActions from '~~/app/components/known-players/KnownPlayersConflictActions.vue'
import KnownPlayersConflictList from '~~/app/components/known-players/KnownPlayersConflictList.vue'
import KnownPlayersConflicts from '~~/app/components/known-players/KnownPlayersConflicts.vue'
import KnownPlayersPersonaCard from '~~/app/components/known-players/KnownPlayersPersonaCard.vue'
import { conflict, persona } from './fixtures'

// Nuxt UI and the ops composables are auto-imported in the app; here they are stand-ins and globals.
const stub = (tag: string, text?: (props: Record<string, unknown>) => string) => defineComponent({
  props: ['label', 'title', 'description'],
  setup(props, { slots, attrs }) {
    return () => h(tag, attrs, [text ? text(props) : '', slots.header?.(), slots.default?.(), slots.body?.()])
  },
})

const ui = {
  UBadge: stub('mark', p => String(p.label ?? '')),
  UButton: stub('button', p => String(p.label ?? '')),
  UCard: stub('section'),
  UInput: stub('span'),
  USelect: stub('span'),
  UPagination: stub('nav'),
  USlideover: stub('div'),
  USeparator: stub('hr'),
  UIcon: stub('i'),
  FetchErrorAlert: stub('aside', p => String(p.title ?? '')),
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('KnownPlayersConflicts', () => {
  it('opens the conflict a deep link names, settles it, and drops the link on close', async () => {
    const query = ref<Record<string, unknown>>({ status: 'open', conflict: 'c1' })
    const replace = vi.fn(({ query: next }: { query: Record<string, unknown> }) => {
      query.value = next
    })
    const refresh = vi.fn()
    const toast = { success: vi.fn(), warning: vi.fn(), failure: vi.fn() }
    const getPersonaConflict = vi.fn().mockResolvedValue({ conflict: conflict(), claimant: persona({ id: 'p2' }), owner: persona() })
    const resolvePersonaConflict = vi.fn().mockResolvedValue({ id: 'c1', resolution: 'Kept', holderSlug: 'peng', mergedSlug: null, alsoResolved: 0 })
    vi.stubGlobal('usePersonaConflicts', () => ({
      data: ref({ conflicts: [conflict()], total: 1, page: 1, pageSize: 25, open: 1, resolved: 0 }),
      pending: ref(false),
      error: ref(null),
      refresh,
    }))
    vi.stubGlobal('getPersonaConflict', getPersonaConflict)
    vi.stubGlobal('resolvePersonaConflict', resolvePersonaConflict)
    vi.stubGlobal('useActionToast', () => toast)
    vi.stubGlobal('useRoute', () => ({ get query() { return query.value } }))
    vi.stubGlobal('useRouter', () => ({ replace }))
    vi.stubGlobal('refDebounced', (source: unknown) => source)
    vi.stubGlobal('ALL', 'all')

    const wrapper = mount(KnownPlayersConflicts, {
      props: { status: 'Open' },
      global: { components: { ...ui, KnownPlayersConflictList, KnownPlayersConflictActions, KnownPlayersPersonaCard } },
    })
    await flushPromises()
    expect(getPersonaConflict).toHaveBeenCalledWith('c1')
    expect(wrapper.findAllComponents(KnownPlayersPersonaCard)).toHaveLength(2)
    expect(wrapper.findAll('[data-row]')).toHaveLength(1)

    wrapper.findComponent(KnownPlayersConflictActions).vm.$emit('resolve', { action: 'keep' })
    await flushPromises()
    expect(resolvePersonaConflict).toHaveBeenCalledWith('c1', { action: 'keep' })
    expect(toast.success).toHaveBeenCalledWith('Account kept by peng', undefined)
    expect(refresh).toHaveBeenCalled()
    expect(replace).toHaveBeenCalledWith({ query: { status: 'open', conflict: undefined } })
  })

  it('reports a failed resolution', async () => {
    const toast = { success: vi.fn(), warning: vi.fn(), failure: vi.fn() }
    vi.stubGlobal('usePersonaConflicts', () => ({ data: ref(null), pending: ref(false), error: ref(null), refresh: vi.fn() }))
    vi.stubGlobal('getPersonaConflict', vi.fn().mockResolvedValue({ conflict: conflict(), claimant: persona({ id: 'p2' }), owner: persona() }))
    vi.stubGlobal('resolvePersonaConflict', vi.fn().mockRejectedValue({ data: { detail: 'This conflict is already resolved.' } }))
    vi.stubGlobal('useActionToast', () => toast)
    vi.stubGlobal('useRoute', () => ({ query: { conflict: 'c1' } }))
    vi.stubGlobal('useRouter', () => ({ replace: vi.fn() }))
    vi.stubGlobal('refDebounced', (source: unknown) => source)
    vi.stubGlobal('ALL', 'all')

    const wrapper = mount(KnownPlayersConflicts, {
      props: { status: 'Open' },
      global: { components: { ...ui, KnownPlayersConflictList, KnownPlayersConflictActions, KnownPlayersPersonaCard } },
    })
    await flushPromises()
    wrapper.findComponent(KnownPlayersConflictActions).vm.$emit('resolve', { action: 'keep' })
    await flushPromises()
    expect(toast.failure).toHaveBeenCalledWith('Conflict not resolved', 'This conflict is already resolved.')
    expect(wrapper.text()).toContain('No open conflict')
  })
})
