import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref, watch } from 'vue'
import type { Kit, KitStore } from '#shared/types/kit'
import KitsPage from '../app/pages/kits.vue'
import KitNew from '../app/components/kit/KitNew.vue'
import KitDetail from '../app/components/kit/KitDetail.vue'
import { useKitAction } from '../app/composables/useKitAction'
import { kit, RUNE_TREE } from './kit-fixtures'

/**
 * The My kits page (#2205) outside Nuxt: every kit grouped by champion, the
 * kit's detail and its "use" mark on all its positions, and a new kit made for
 * a champion picked and several positions. Nuxt UI and the site's pickers are
 * plain elements the test drives.
 */
const global = {
  components: { KitNew, KitDetail },
  stubs: {
    KitList: { props: ['kits', 'activeIds', 'modelValue', 'showPositions'], emits: ['update:modelValue'], template: '<ul class="list"><li v-for="k in kits" :key="k.id"><button class="pick" :data-active="activeIds.includes(k.id)" @click="$emit(\'update:modelValue\', k.id)">{{ k.name }}</button></li></ul>' },
    KitEditor: { props: ['kit'], emits: ['save', 'cancel'], template: '<div class="editor" :data-name="kit.name" :data-positions="kit.positions.join()"><button class="save" @click="$emit(\'save\', kit)" /><button class="cancel" @click="$emit(\'cancel\')" /></div>' },
    KitBuild: { props: ['kit'], template: '<div class="kit-build">{{ kit.name }}</div>' },
    ChampionPicker: { props: ['championId', 'champions'], emits: ['update:championId'], template: '<button class="champion" :data-count="champions.length" @click="$emit(\'update:championId\', 103)" />' },
    RolePicker: { props: ['positions'], emits: ['update:positions'], template: '<button class="roles" @click="$emit(\'update:positions\', [\'TOP\', \'MIDDLE\'])" />' },
    UFormField: { template: '<label><slot /></label>' },
    UButton: { props: ['label', 'disabled', 'loading'], emits: ['click'], template: '<button class="u-button" :disabled="disabled" @click="$emit(\'click\')">{{ label }}</button>' },
    UBadge: { props: ['label'], template: '<span class="badge">{{ label }}</span>' },
    UIcon: true,
    UInput: true,
    UDropdownMenu: true,
    UModal: { props: ['open', 'title', 'description'], emits: ['update:open'], template: '<div v-if="open" class="modal"><h2>{{ title }}</h2><slot name="body" /><slot name="footer" /></div>' },
  },
}

const store = ref<KitStore>({ kits: [], active: {} })
const kitsApi = {
  store,
  error: ref<string | null>(null),
  activeOn: (k: Kit) => k.positions.filter(position => store.value.active[`${k.championId}:${position}`] === k.id),
  save: vi.fn(async () => {}),
  setActive: vi.fn(async () => {}),
  remove: vi.fn(async () => {}),
  duplicate: vi.fn(async () => null),
  rename: vi.fn(async () => {}),
}

beforeEach(() => {
  store.value = { kits: [], active: {} }
  kitsApi.save.mockClear()
  kitsApi.setActive.mockClear()
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('ref', ref)
  vi.stubGlobal('watch', watch)
  vi.stubGlobal('useKits', () => kitsApi)
  vi.stubGlobal('useKitAction', useKitAction)
  vi.stubGlobal('useToast', () => ({ add: () => {} }))
  vi.stubGlobal('useChampionStatics', () => ({ nameOf: (id: number) => ({ 103: 'Ahri', 777: 'Yone' })[id] ?? '', portraitOf: () => null }))
  vi.stubGlobal('useStaticChampionsById', () => computed(() => new Map([[103, { championId: 103, name: 'Ahri', iconUrl: '' }]])))
  vi.stubGlobal('useStaticData', () => ({ runeTree: ref(RUNE_TREE) }))
  vi.stubGlobal('useChampionBuilds', (championId: { value: number | null }, position: { value: string | null }) => ({
    response: computed(() => (championId.value && position.value
      ? { builds: [{ core: { itemPath: { itemIds: [6655, 3157], games: 1, pickRate: 1, winRate: 1 }, boots: null, starterItems: null, summonerSpells: null, skillOrder: null, runePage: null } }] }
      : null)),
    pending: ref(false),
  }))
})

afterEach(() => vi.unstubAllGlobals())

describe('My kits', () => {
  it('lists every kit by champion, and marks the one shown on all its positions', async () => {
    store.value = {
      kits: [kit({ id: 'y1', championId: 777, name: 'Yone bruiser', positions: ['TOP', 'MIDDLE'] }), kit()],
      active: { '777:TOP': 'y1' },
    }
    const wrapper = mount(KitsPage, { global })
    expect(wrapper.findAll('h2 .stat-label').map(label => label.text())).toEqual(['Ahri', 'Yone'])
    expect(wrapper.get('.kit-build').text()).toBe('Into tanks')

    await wrapper.findAll('.pick').find(button => button.text() === 'Yone bruiser')!.trigger('click')
    expect(wrapper.get('.badge').text()).toBe('Active on Top')
    // Used on one of its two positions: "use" marks it on both.
    await wrapper.findAll('.u-button').find(button => button.text() === 'Use in champion select')!.trigger('click')
    expect(kitsApi.setActive).toHaveBeenCalledWith(777, ['TOP', 'MIDDLE'], 'y1')

    store.value = { ...store.value, active: { '777:TOP': 'y1', '777:MIDDLE': 'y1' } }
    await flushPromises()
    await wrapper.findAll('.u-button').find(button => button.text() === 'Used in champion select')!.trigger('click')
    expect(kitsApi.setActive).toHaveBeenLastCalledWith(777, ['TOP', 'MIDDLE'], null)

    await wrapper.findAll('.u-button').find(button => button.text() === 'Edit')!.trigger('click')
    await wrapper.get('.save').trigger('click')
    await flushPromises()
    expect(kitsApi.save).toHaveBeenCalledWith(expect.objectContaining({ id: 'y1' }))
    expect(wrapper.find('.editor').exists()).toBe(false)
  })

  it('makes a kit for the champion and positions picked, from TrueMain\'s build on the first', async () => {
    const wrapper = mount(KitsPage, { global })
    expect(wrapper.text()).toContain('Your own builds')
    await wrapper.findAll('.u-button').find(button => button.text() === 'New kit')!.trigger('click')

    const create = () => wrapper.findAll('.modal .u-button').find(button => button.text() === 'Create')!
    expect(create().attributes('disabled')).toBeDefined()
    expect(wrapper.get('.champion').attributes('data-count')).toBe('1')
    await wrapper.get('.champion').trigger('click')
    await wrapper.get('.roles').trigger('click')
    expect(create().attributes('disabled')).toBeUndefined()
    await create().trigger('click')

    expect(wrapper.find('.modal').exists()).toBe(false)
    const editor = wrapper.get('.editor')
    expect(editor.attributes('data-name')).toBe('My Top · Mid')
    expect(editor.attributes('data-positions')).toBe('TOP,MIDDLE')
    await wrapper.get('.save').trigger('click')
    await flushPromises()
    expect(kitsApi.save).toHaveBeenCalledWith(expect.objectContaining({ championId: 103, positions: ['TOP', 'MIDDLE'], tree: [{ itemId: 6655, children: [{ itemId: 3157, children: [] }] }] }))
  })
})
