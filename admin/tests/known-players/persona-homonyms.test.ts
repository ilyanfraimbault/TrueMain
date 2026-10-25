import type { PersonaHomonymGroup } from '~~/shared/types/ops'
import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import KnownPlayersConflictActions from '~~/app/components/known-players/KnownPlayersConflictActions.vue'
import KnownPlayersConflictList from '~~/app/components/known-players/KnownPlayersConflictList.vue'
import KnownPlayersConflicts from '~~/app/components/known-players/KnownPlayersConflicts.vue'
import KnownPlayersHomonymGroup from '~~/app/components/known-players/KnownPlayersHomonymGroup.vue'
import KnownPlayersHomonyms from '~~/app/components/known-players/KnownPlayersHomonyms.vue'
import KnownPlayersPersonaCard from '~~/app/components/known-players/KnownPlayersPersonaCard.vue'
import { autoMergedMessage, markedDistinct, markedMessage, mergedMessage } from '~~/shared/utils/persona-homonyms'
import { conflict, persona } from './fixtures'

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

function group(over: Partial<PersonaHomonymGroup> = {}): PersonaHomonymGroup {
  return {
    key: 'rain',
    personas: [
      persona({ id: 'p1', slug: 'rain', displayName: 'Rain' }),
      persona({ id: 'p2', slug: 'rain-dpm', displayName: 'rain', source: 'Dpm' }),
      persona({ id: 'p3', slug: 'rain-helena-ruiz', displayName: 'Rain' }),
    ],
    distinctPairs: [],
    ...over,
  }
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('persona-homonyms utils', () => {
  it('words the automatic merge', () => {
    expect(autoMergedMessage({ merged: 0, alsoResolved: 0, skipped: 0 })).toEqual({ title: 'No same-name conflict to merge', description: undefined })
    expect(autoMergedMessage({ merged: 0, alsoResolved: 0, skipped: 2 }).description).toBe('2 conflicts changed meanwhile; reload the queue.')
    expect(autoMergedMessage({ merged: 1, alsoResolved: 0, skipped: 0 })).toEqual({ title: '1 same-name conflict merged', description: undefined })
    expect(autoMergedMessage({ merged: 3, alsoResolved: 1, skipped: 1 })).toEqual({
      title: '3 same-name conflicts merged',
      description: '1 other conflict settled with them. 1 conflict changed meanwhile and stay open.',
    })
  })

  it('words the merge and the mark', () => {
    expect(mergedMessage({ keptSlug: 'rain', mergedSlugs: ['rain-dpm'], alsoResolved: 0 })).toEqual({
      title: 'rain-dpm merged into rain',
      description: '/players/rain-dpm now redirects.',
    })
    expect(mergedMessage({ keptSlug: 'rain', mergedSlugs: ['a', 'b'], alsoResolved: 2 }).description)
      .toBe('/players/a, /players/b now redirect. 2 open conflicts settled with it.')
    expect(markedMessage({ marked: 1 }).title).toBe('Marked as different people')
    expect(markedMessage({ marked: 0 }).title).toBe('Already marked as different people')
  })

  it('reads a pair in either order', () => {
    const marked = group({ distinctPairs: [['p1', 'p3']] })
    expect(markedDistinct(marked, 'p3', 'p1')).toBe(true)
    expect(markedDistinct(marked, 'p1', 'p2')).toBe(false)
  })
})

describe('KnownPlayersHomonymGroup', () => {
  const mountGroup = (props: { group: PersonaHomonymGroup, busy?: boolean }) => mount(KnownPlayersHomonymGroup, {
    props: { busy: false, ...props },
    global: { components: { ...ui, KnownPlayersPersonaCard } },
  })

  it('keeps the oldest by default and merges the others after a confirmation', async () => {
    const wrapper = mountGroup({ group: group() })
    expect(wrapper.findAllComponents(KnownPlayersPersonaCard)).toHaveLength(3)
    const mergeButton = () => wrapper.get('[data-action="merge"]')
    expect(mergeButton().text()).toBe('Merge 2 into rain')

    await mergeButton().trigger('click')
    expect(wrapper.emitted('merge')).toBeUndefined()
    expect(mergeButton().text()).toBe('Confirm: merge 2 into rain')
    await mergeButton().trigger('click')
    expect(wrapper.emitted('merge')).toEqual([[{ keepId: 'p1', mergeIds: ['p2', 'p3'] }]])
  })

  it('merges a selection into another kept player, and marks a selection as different people', async () => {
    const wrapper = mountGroup({ group: group() })
    await wrapper.get('[data-persona="rain-helena-ruiz"] [data-select]').trigger('click')
    await wrapper.get('[data-persona="rain-dpm"] [data-keep]').trigger('click')
    expect(wrapper.get('[data-action="merge"]').text()).toBe('Merge 1 into rain-dpm')
    await wrapper.get('[data-action="merge"]').trigger('click')
    await wrapper.get('[data-action="merge"]').trigger('click')
    expect(wrapper.emitted('merge')).toEqual([[{ keepId: 'p2', mergeIds: ['p1'] }]])

    await wrapper.get('[data-persona="rain-helena-ruiz"] [data-select]').trigger('click')
    await wrapper.get('[data-action="distinct"]').trigger('click')
    expect(wrapper.emitted('distinct')).toEqual([[{ personaIds: ['p1', 'p2', 'p3'] }]])
  })

  it('shows the pairs already marked, and sends nothing while busy or with one player left', async () => {
    const wrapper = mountGroup({ group: group({ distinctPairs: [['p1', 'p3']] }) })
    expect(wrapper.text()).toContain('1 pair already marked as different people.')
    expect(wrapper.get('[data-persona="rain"]').text()).toContain('Different from rain-helena-ruiz')

    await wrapper.get('[data-persona="rain-dpm"] [data-select]').trigger('click')
    await wrapper.get('[data-persona="rain-helena-ruiz"] [data-select]').trigger('click')
    expect(wrapper.get('[data-action="merge"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-action="distinct"]').attributes('disabled')).toBeDefined()

    await wrapper.setProps({ busy: true, group: group() })
    await wrapper.get('[data-action="merge"]').trigger('click')
    expect(wrapper.emitted('merge')).toBeUndefined()
  })
})

describe('KnownPlayersHomonyms', () => {
  function stubQueue(data: unknown) {
    const refresh = vi.fn()
    const toast = { success: vi.fn(), warning: vi.fn(), failure: vi.fn() }
    vi.stubGlobal('usePersonaHomonyms', () => ({ data: ref(data), pending: ref(false), error: ref(null), refresh }))
    vi.stubGlobal('useActionToast', () => toast)
    vi.stubGlobal('refDebounced', (source: unknown) => source)
    return { refresh, toast }
  }

  const mountQueue = () => mount(KnownPlayersHomonyms, {
    global: { components: { ...ui, KnownPlayersHomonymGroup, KnownPlayersPersonaCard } },
  })

  it('merges a group and marks one as different people', async () => {
    const { refresh, toast } = stubQueue({ groups: [group()], total: 1, page: 1, pageSize: 10 })
    const merge = vi.fn().mockResolvedValue({ keptSlug: 'rain', mergedSlugs: ['rain-dpm'], alsoResolved: 0 })
    const mark = vi.fn().mockResolvedValue({ marked: 3 })
    vi.stubGlobal('mergePersonaHomonyms', merge)
    vi.stubGlobal('markPersonaHomonymsDistinct', mark)

    const wrapper = mountQueue()
    expect(wrapper.text()).toContain('1 name')
    const groupComponent = wrapper.findComponent(KnownPlayersHomonymGroup)
    groupComponent.vm.$emit('merge', { keepId: 'p1', mergeIds: ['p2'] })
    await flushPromises()
    expect(merge).toHaveBeenCalledWith({ keepId: 'p1', mergeIds: ['p2'] })
    expect(toast.success).toHaveBeenCalledWith('rain-dpm merged into rain', '/players/rain-dpm now redirects.')

    groupComponent.vm.$emit('distinct', { personaIds: ['p1', 'p3'] })
    await flushPromises()
    expect(mark).toHaveBeenCalledWith({ personaIds: ['p1', 'p3'] })
    expect(toast.success).toHaveBeenCalledWith('Marked as different people', undefined)
    expect(refresh).toHaveBeenCalledTimes(2)
  })

  it('reports a refused action and an empty queue', async () => {
    const { toast } = stubQueue({ groups: [group()], total: 1, page: 1, pageSize: 10 })
    vi.stubGlobal('mergePersonaHomonyms', vi.fn().mockRejectedValue({ data: { detail: 'A known player was merged meanwhile.' } }))
    const wrapper = mountQueue()
    wrapper.findComponent(KnownPlayersHomonymGroup).vm.$emit('merge', { keepId: 'p1', mergeIds: ['p2'] })
    await flushPromises()
    expect(toast.failure).toHaveBeenCalledWith('Not merged', 'A known player was merged meanwhile.')

    stubQueue({ groups: [], total: 0, page: 1, pageSize: 10 })
    expect(mountQueue().text()).toContain('No homonym left')
  })
})

describe('KnownPlayersConflicts auto-merge', () => {
  it('sweeps the same-name conflicts and reloads the queue', async () => {
    const refresh = vi.fn()
    const toast = { success: vi.fn(), warning: vi.fn(), failure: vi.fn() }
    const autoMerge = vi.fn().mockResolvedValue({ merged: 2, alsoResolved: 0, skipped: 0 })
    vi.stubGlobal('usePersonaConflicts', () => ({
      data: ref({ conflicts: [conflict()], total: 1, page: 1, pageSize: 25, open: 1, resolved: 0 }),
      pending: ref(false),
      error: ref(null),
      refresh,
    }))
    vi.stubGlobal('autoMergePersonaConflicts', autoMerge)
    vi.stubGlobal('useActionToast', () => toast)
    vi.stubGlobal('useRoute', () => ({ query: {} }))
    vi.stubGlobal('useRouter', () => ({ replace: vi.fn() }))
    vi.stubGlobal('refDebounced', (source: unknown) => source)
    vi.stubGlobal('ALL', 'all')

    const wrapper = mount(KnownPlayersConflicts, {
      props: { status: 'Open' },
      global: { components: { ...ui, KnownPlayersConflictList, KnownPlayersConflictActions, KnownPlayersPersonaCard } },
    })
    await wrapper.get('[data-action="auto-merge"]').trigger('click')
    await flushPromises()
    expect(autoMerge).toHaveBeenCalled()
    expect(toast.success).toHaveBeenCalledWith('2 same-name conflicts merged', undefined)
    expect(refresh).toHaveBeenCalled()

    autoMerge.mockRejectedValueOnce({ data: { title: 'Unavailable' } })
    await wrapper.get('[data-action="auto-merge"]').trigger('click')
    await flushPromises()
    expect(toast.failure).toHaveBeenCalledWith('Same-name conflicts not merged', 'Unavailable')

    await wrapper.setProps({ status: 'Resolved' })
    expect(wrapper.find('[data-action="auto-merge"]').exists()).toBe(false)
  })
})
