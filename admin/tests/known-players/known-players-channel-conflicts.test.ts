import type { Ref } from 'vue'
import type { PersonaChannelConflict } from '~~/shared/types/ops'
import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import KnownPlayersChannelConflicts from '~~/app/components/known-players/KnownPlayersChannelConflicts.vue'
import KnownPlayersConflictActions from '~~/app/components/known-players/KnownPlayersConflictActions.vue'
import KnownPlayersConflictCompare from '~~/app/components/known-players/KnownPlayersConflictCompare.vue'
import KnownPlayersConflictList from '~~/app/components/known-players/KnownPlayersConflictList.vue'
import KnownPlayersConflicts from '~~/app/components/known-players/KnownPlayersConflicts.vue'
import { conflict, persona } from './fixtures'

// Nuxt UI and the ops composables are auto-imported in the app; here they are stand-ins and globals.
const stub = (tag: string, text?: (props: Record<string, unknown>) => string) => defineComponent({
  props: ['label', 'title', 'description'],
  emits: ['click'],
  setup(props, { slots, attrs, emit }) {
    return () => h(tag, { ...attrs, onClick: () => emit('click') }, [text ? text(props) : '', slots.header?.(), slots.default?.(), slots.body?.()])
  },
})

const ui = {
  UBadge: stub('mark', p => String(p.label ?? '')),
  UButton: stub('button', p => String(p.label ?? '')),
  UCard: stub('section'),
  UInput: stub('span'),
  USelect: stub('span'),
  UPagination: stub('nav'),
  USlideover: stub('div', p => String(p.title ?? '')),
  USeparator: stub('hr'),
  UIcon: stub('i'),
  FetchErrorAlert: stub('aside', p => String(p.title ?? '')),
}

const irelking: PersonaChannelConflict = {
  older: persona({ id: 'p1', slug: 'irelking', displayName: 'IRELKING', source: 'DeepLol', links: [{ kind: 'chzzk', url: 'https://chzzk.naver.com/993db7ed', source: 'DeepLol' }] }),
  newer: persona({ id: 'p2', slug: 'kasumi', displayName: 'Kasumi', source: 'Dpm', accounts: [], links: [{ kind: 'chzzk', url: 'https://chzzk.naver.com/993db7ed', source: 'Dpm' }] }),
  sharedChannels: [{ kind: 'chzzk', url: 'https://chzzk.naver.com/993db7ed' }],
}

function stubPanel(conflicts: PersonaChannelConflict[]) {
  const refresh = vi.fn()
  const toast = { success: vi.fn(), warning: vi.fn(), failure: vi.fn() }
  const usePersonaChannelConflicts = vi.fn(() => ({
    data: ref({ conflicts, total: conflicts.length, page: 1, pageSize: 25 }),
    pending: ref(false),
    error: ref(null),
    refresh,
  }))
  vi.stubGlobal('usePersonaChannelConflicts', usePersonaChannelConflicts)
  vi.stubGlobal('useActionToast', () => toast)
  return { refresh, toast, usePersonaChannelConflicts }
}

function mountPanel(search?: string) {
  return mount(KnownPlayersChannelConflicts, {
    props: { search },
    global: { components: { ...ui, KnownPlayersConflictCompare } },
  })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('KnownPlayersChannelConflicts', () => {
  it('lists the pairs with their shared channels and merges after a confirmation', async () => {
    const { refresh, toast } = stubPanel([irelking])
    const mergePersonaHomonyms = vi.fn().mockResolvedValue({ keptSlug: 'irelking', mergedSlugs: ['kasumi'], alsoResolved: 0 })
    vi.stubGlobal('mergePersonaHomonyms', mergePersonaHomonyms)

    const wrapper = mountPanel()
    expect(wrapper.findAll('[data-channel-row]')).toHaveLength(1)
    expect(wrapper.text()).toContain('chzzk · 993db7ed')

    await wrapper.find('[data-channel-row]').trigger('click')
    expect(wrapper.findAllComponents(KnownPlayersConflictCompare)).toHaveLength(1)
    const mergeOlder = () => wrapper.find('[data-action="merge-older"]')
    expect(mergeOlder().text()).toBe('Merge into irelking')

    await mergeOlder().trigger('click')
    expect(mergePersonaHomonyms).not.toHaveBeenCalled()
    expect(mergeOlder().text()).toBe('Confirm: merge into irelking')

    await mergeOlder().trigger('click')
    await flushPromises()
    expect(mergePersonaHomonyms).toHaveBeenCalledWith({ keepId: 'p1', mergeIds: ['p2'] })
    expect(toast.success).toHaveBeenCalledWith('kasumi merged into irelking', '/players/kasumi now redirects.')
    expect(refresh).toHaveBeenCalled()
  })

  it('merges into the newer one and marks different people', async () => {
    const { toast } = stubPanel([irelking])
    const mergePersonaHomonyms = vi.fn().mockRejectedValue({ data: { detail: 'A known player is unknown, or was merged or removed meanwhile; reload the group.' } })
    const markPersonaHomonymsDistinct = vi.fn().mockResolvedValue({ marked: 1 })
    vi.stubGlobal('mergePersonaHomonyms', mergePersonaHomonyms)
    vi.stubGlobal('markPersonaHomonymsDistinct', markPersonaHomonymsDistinct)

    const wrapper = mountPanel()
    await wrapper.find('[data-channel-row]').trigger('keydown', { key: 'Enter' })
    await wrapper.find('[data-action="merge-newer"]').trigger('click')
    await wrapper.find('[data-action="merge-newer"]').trigger('click')
    await flushPromises()
    expect(mergePersonaHomonyms).toHaveBeenCalledWith({ keepId: 'p2', mergeIds: ['p1'] })
    expect(toast.failure).toHaveBeenCalledWith('Not merged', 'A known player is unknown, or was merged or removed meanwhile; reload the group.')

    await wrapper.find('[data-action="distinct"]').trigger('click')
    await flushPromises()
    expect(markPersonaHomonymsDistinct).toHaveBeenCalledWith({ personaIds: ['p1', 'p2'] })
    expect(toast.success).toHaveBeenCalledWith('Marked as different people', undefined)
  })

  it('passes the search on and says when nothing matches', () => {
    const { usePersonaChannelConflicts } = stubPanel([])

    const wrapper = mountPanel('faker')

    const filters = (usePersonaChannelConflicts.mock.calls[0] as unknown as [Ref<Record<string, unknown>>])[0]
    expect(filters.value).toEqual({ search: 'faker', page: 1, pageSize: 25 })
    expect(wrapper.find('[data-empty]').text()).toBe('No channel conflict matches this search.')
  })

  it('counts the channel conflicts in the open queue and reloads them with it', async () => {
    const { refresh } = stubPanel([irelking])
    vi.stubGlobal('usePersonaConflicts', () => ({
      data: ref({ conflicts: [conflict()], total: 1, page: 1, pageSize: 25, open: 1, resolved: 4 }),
      pending: ref(false),
      error: ref(null),
      refresh: vi.fn(),
    }))
    vi.stubGlobal('useRoute', () => ({ query: {} }))
    vi.stubGlobal('useRouter', () => ({ replace: vi.fn() }))
    vi.stubGlobal('refDebounced', (source: unknown) => source)
    vi.stubGlobal('ALL', 'all')

    const wrapper = mount(KnownPlayersConflicts, {
      props: { status: 'Open' },
      global: { components: { ...ui, KnownPlayersConflictList, KnownPlayersConflictActions, KnownPlayersConflictCompare, KnownPlayersChannelConflicts } },
    })
    await flushPromises()

    expect((wrapper.vm as unknown as { counts: { open: number, resolved: number } }).counts).toEqual({ open: 2, resolved: 4 })
    expect(wrapper.findAll('[data-channel-row]')).toHaveLength(1)

    ;(wrapper.vm as unknown as { refresh: () => void }).refresh()
    await flushPromises()
    expect(refresh).toHaveBeenCalled()
  })
})
