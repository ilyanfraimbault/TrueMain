import { mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { computed, defineComponent, h, ref, useTemplateRef } from 'vue'
import DefaultLayout from '~~/app/layouts/default.vue'

// The layout hands the ⌘K palette the groups `usePaletteSearch` builds for the
// typed term (#2290), and the term back as the palette's model.

const passthrough = (name: string) => defineComponent({
  name,
  inheritAttrs: false,
  setup(_, { slots }) {
    return () => h('div', [slots.default?.({ collapsed: false }), slots.header?.({ collapsed: false }), slots.footer?.({ collapsed: false })])
  },
})

const DashboardSearch = defineComponent({
  name: 'UDashboardSearch',
  props: ['groups', 'loading', 'searchTerm', 'placeholder'],
  emits: ['update:searchTerm'],
  setup: () => () => h('dialog'),
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('default layout', () => {
  it('feeds the palette the term-driven groups and their loading state', async () => {
    const term = ref('')
    const groups = computed(() => [{ id: term.value ? 'entity-match' : 'monitor', items: [] }])
    vi.stubGlobal('usePaletteSearch', () => ({ term, groups, loading: ref(true) }))
    vi.stubGlobal('useNavSummary', () => ({ summary: ref({ openConflicts: 2, homonymGroups: 0, healthStatus: 'green', healthEvaluatedAtUtc: null }) }))
    vi.stubGlobal('useTemplateRef', useTemplateRef)
    vi.stubGlobal('useEventListener', vi.fn())
    vi.stubGlobal('computed', computed)
    vi.stubGlobal('ref', ref)
    vi.stubGlobal('$fetch', vi.fn())

    const components = Object.fromEntries(
      ['UDashboardGroup', 'UDashboardSidebar', 'UDashboardSearchButton', 'UNavigationMenu', 'UIcon', 'UserMenu', 'OpsCacheAgeNotice']
        .map(name => [name, passthrough(name)]),
    )
    const wrapper = mount(DefaultLayout, { global: { components: { ...components, UDashboardSearch: DashboardSearch } } })
    const search = wrapper.findComponent(DashboardSearch)
    expect(search.props('loading')).toBe(true)
    expect(search.props('placeholder')).toContain('Riot ID')
    expect(search.props('groups')).toEqual([{ id: 'monitor', items: [] }])

    const menu = wrapper.findComponent({ name: 'UNavigationMenu' })
    // The sidebar carries the nav summary's badges (#2294).
    expect(JSON.stringify(menu.vm.$attrs.items)).toContain('"label":2')

    search.vm.$emit('update:searchTerm', 'EUW1_7012345678')
    await wrapper.vm.$nextTick()
    expect(term.value).toBe('EUW1_7012345678')
    expect(search.props('groups')).toEqual([{ id: 'entity-match', items: [] }])
  })
})
