import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref, toRef, watch } from 'vue'
import type { Kit } from '#shared/types/kit'
import KitEditor from '../app/components/kit/KitEditor.vue'
import KitPanel from '../app/components/kit/KitPanel.vue'
import KitBuild from '../app/components/kit/KitBuild.vue'
import { item, kit, PAGE, RUNE_TREE } from './kit-fixtures'

// The editor, the champion page's panel and the kit's view outside Nuxt: the
// children that draw icons are plain elements, the item picker a stand-in the
// test picks through.
let nextPick = 3157
const picker = {
  props: ['open', 'groups', 'blocked', 'title'],
  emits: ['pick', 'update:open'],
  methods: { next: () => nextPick },
  template: '<div v-if="open" class="picker" :data-title="title" :data-blocked="blocked.join()" :data-groups="groups.join()"><button class="pick" @click="$emit(\'pick\', next())" /></div>',
}
const tree = { props: ['tree', 'editable'], emits: ['pick', 'remove'], template: '<div class="tree" :data-roots="tree.map(n => n.itemId).join()"><button class="branch" @click="$emit(\'pick\', { at: [0], mode: \'child\' })" /><button class="replace" @click="$emit(\'pick\', { at: [0, 1], mode: \'replace\' })" /><button class="drop" @click="$emit(\'remove\', [0, 1])" /></div>' }
const global = {
  stubs: {
    GameTooltipItemIcon: true,
    KitSpells: true,
    KitRunePage: true,
    KitItemPicker: picker,
    KitTree: tree,
    KitList: { props: ['kits', 'activeId', 'modelValue'], template: '<ul class="list"><li v-for="k in kits" :key="k.id">{{ k.name }}</li></ul>' },
    KitBuild: { props: ['kit'], template: '<div class="kit-build">{{ kit.name }}</div>' },
    KitEditor: { props: ['kit'], emits: ['save', 'cancel'], template: '<div class="editor" :data-name="kit.name"><button class="save" @click="$emit(\'save\', kit)" /><button class="cancel" @click="$emit(\'cancel\')" /></div>' },
    BuildCore: { props: ['summonerSpells', 'runePage', 'championName'], template: '<div class="core" :data-champion="championName"><slot name="spells" /></div>' },
    UFormField: { template: '<label><slot /></label>' },
    UInput: { props: ['modelValue'], emits: ['update:modelValue'], template: '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)">' },
    UButton: { props: ['label', 'disabled', 'ariaLabel'], emits: ['click'], template: '<button class="u-button" :disabled="disabled" :aria-label="ariaLabel" @click="$emit(\'click\')">{{ label }}</button>' },
    UBadge: { props: ['label'], template: '<span class="badge">{{ label }}</span>' },
    UIcon: true,
    USkeleton: true,
    UTooltip: { template: '<div><slot /></div>' },
    UDropdownMenu: { props: ['items'], template: '<div class="menu"><button v-for="entry in items[0]" :key="entry.label" class="menu-item" @click="entry.onSelect()">{{ entry.label }}</button></div>' },
    UModal: { props: ['open', 'title', 'description'], emits: ['update:open'], template: '<div v-if="open" class="modal"><h2>{{ title }}</h2><p>{{ description }}</p><slot name="body" /><slot name="footer" /></div>' },
  },
}

const items = ref({ 6655: item(6655), 3020: item(3020), 1056: item(1056), 3157: item(3157), 3111: item(3111) })
const kitsApi = {
  kitsFor: vi.fn(() => [] as Kit[]),
  activeIdFor: vi.fn(() => null as string | null),
  save: vi.fn(async () => {}),
  remove: vi.fn(async () => {}),
  duplicate: vi.fn(async () => null as Kit | null),
  rename: vi.fn(async () => {}),
  setActive: vi.fn(async () => {}),
  error: ref<string | null>(null),
}
const kitImport = { available: ref(true), inChampSelect: ref(true), pending: ref(null), importItems: vi.fn(), importSpells: vi.fn() }
let toasts: { title: string }[]

beforeEach(() => {
  toasts = []
  nextPick = 3157
  for (const fn of [kitsApi.kitsFor, kitsApi.activeIdFor, kitsApi.save, kitsApi.remove, kitsApi.duplicate, kitsApi.rename, kitsApi.setActive, kitImport.importItems, kitImport.importSpells]) fn.mockClear()
  kitsApi.kitsFor.mockReturnValue([])
  kitsApi.activeIdFor.mockReturnValue(null)
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('ref', ref)
  vi.stubGlobal('watch', watch)
  vi.stubGlobal('toRef', toRef)
  vi.stubGlobal('useStaticData', () => ({ items, summoners: ref({}), runeTree: ref(RUNE_TREE), pending: ref(false) }))
  vi.stubGlobal('useChampionStatics', () => ({ nameOf: () => 'Ahri' }))
  vi.stubGlobal('useKits', () => kitsApi)
  vi.stubGlobal('useKitImport', () => kitImport)
  vi.stubGlobal('useToast', () => ({ add: (toast: { title: string }) => toasts.push(toast) }))
  vi.stubGlobal('useChampionBuilds', () => ({
    response: ref({ builds: [{ core: { itemPath: { itemIds: [6655, 3157], games: 1, pickRate: 1, winRate: 1 }, boots: null, starterItems: null, summonerSpells: null, skillOrder: null, runePage: null } }] }),
    pending: ref(false),
  }))
})

afterEach(() => vi.unstubAllGlobals())

describe('KitEditor', () => {
  it('edits a draft through the picker and saves it whole', async () => {
    const wrapper = mount(KitEditor, { props: { kit: kit() }, global })
    expect(wrapper.find('.picker').exists()).toBe(false)

    // A branch after the first item: the picker lists items, the path above is blocked.
    await wrapper.get('.branch').trigger('click')
    expect(wrapper.get('.picker').attributes()).toMatchObject({ 'data-title': 'Add an item', 'data-groups': 'legendary,component', 'data-blocked': '6655,3020' })
    await wrapper.get('.pick').trigger('click')
    await wrapper.get('.drop').trigger('click')

    await wrapper.get('.replace').trigger('click')
    expect(wrapper.get('.picker').attributes('data-title')).toBe('Replace the item')
    await wrapper.get('.pick').trigger('click')

    // The starter: replace the first, add one, drop one.
    await wrapper.get('[aria-label="Replace Item 1056"]').trigger('click')
    expect(wrapper.get('.picker').attributes('data-groups')).toBe('starter,component')
    await wrapper.get('.pick').trigger('click')
    await wrapper.get('[aria-label="Add a starter item"]').trigger('click')
    await wrapper.get('.pick').trigger('click')
    await wrapper.get('[aria-label="Remove Item 2003"]').trigger('click')

    // The boots: dropped, then chosen again.
    await wrapper.get('[aria-label="No boots"]').trigger('click')
    await wrapper.get('[aria-label="Choose boots"]').trigger('click')
    expect(wrapper.get('.picker').attributes('data-groups')).toBe('boots')
    nextPick = 3111
    await wrapper.get('.pick').trigger('click')

    await wrapper.get('input').setValue('Into mages')
    const save = wrapper.findAll('.u-button').find(button => button.text() === 'Save kit')!
    await save.trigger('click')
    const saved = wrapper.emitted('save')![0]![0] as Kit
    expect(saved).toMatchObject({ name: 'Into mages', boots: 3111, starter: [3157, 2003, 3157] })
    expect(saved.tree[0]!.children.map(child => child.itemId)).toEqual([3100, 3157])
  })

  it('lists what keeps it from being saved', async () => {
    const wrapper = mount(KitEditor, { props: { kit: kit({ name: '', spells: [4, 4], runes: { ...PAGE, statFlex: 1 } }) }, global })
    expect(wrapper.findAll('li').map(li => li.text())).toEqual(['Name the kit.', 'Choose two different summoner spells.', 'Choose a shard on each row.'])
    const save = wrapper.findAll('.u-button').find(button => button.text() === 'Save kit')!
    expect(save.attributes('disabled')).toBeDefined()
    await wrapper.findAll('.u-button').find(button => button.text() === 'Cancel')!.trigger('click')
    expect(wrapper.emitted('cancel')).toHaveLength(1)
  })
})

describe('KitPanel', () => {
  it('starts a kit from TrueMain\'s build on the lane and saves it', async () => {
    const wrapper = mount(KitPanel, { props: { championId: 103, position: 'MIDDLE' }, global })
    expect(wrapper.text()).toContain('No kit on this lane yet')
    await wrapper.findAll('.u-button').find(button => button.text() === 'New kit')!.trigger('click')
    expect(wrapper.get('.editor').attributes('data-name')).toBe('My Mid')
    await wrapper.get('.save').trigger('click')
    await flushPromises()
    expect(kitsApi.save).toHaveBeenCalledWith(expect.objectContaining({ name: 'My Mid', championId: 103, position: 'MIDDLE' }))
    expect(wrapper.find('.editor').exists()).toBe(false)
  })

  it('marks, copies, renames and deletes the kit on screen', async () => {
    const copy = kit({ id: 'k2', name: 'Into tanks copy' })
    kitsApi.kitsFor.mockReturnValue([kit(), copy])
    kitsApi.duplicate.mockResolvedValue(copy)
    const wrapper = mount(KitPanel, { props: { championId: 103, position: 'MIDDLE' }, global })
    expect(wrapper.get('.kit-build').text()).toBe('Into tanks')

    await wrapper.findAll('.u-button').find(button => button.text() === 'Use in champion select')!.trigger('click')
    expect(kitsApi.setActive).toHaveBeenCalledWith(103, 'MIDDLE', 'k1')

    const menu = (label: string) => wrapper.findAll('.menu-item').find(entry => entry.text() === label)!
    await menu('Duplicate').trigger('click')
    await flushPromises()
    expect(kitsApi.duplicate).toHaveBeenCalledWith('k1')

    await menu('Rename').trigger('click')
    await wrapper.get('.modal input').setValue('Into mages')
    await wrapper.get('.modal form').trigger('submit')
    await flushPromises()
    expect(kitsApi.rename).toHaveBeenCalledWith('k2', 'Into mages')

    await menu('Delete').trigger('click')
    expect(wrapper.get('.modal').text()).toContain('Into tanks copy is removed from this computer.')
    await wrapper.findAll('.modal .u-button').find(button => button.text() === 'Delete')!.trigger('click')
    await flushPromises()
    expect(kitsApi.remove).toHaveBeenCalledWith('k2')

    await wrapper.findAll('.u-button').find(button => button.text() === 'Edit')!.trigger('click')
    expect(wrapper.get('.editor').attributes('data-name')).toBe('Into tanks copy')
    await wrapper.get('.cancel').trigger('click')
    expect(wrapper.find('.editor').exists()).toBe(false)
  })

  it('says when a write failed, and lets the active kit go', async () => {
    kitsApi.kitsFor.mockReturnValue([kit()])
    kitsApi.activeIdFor.mockReturnValue('k1')
    kitsApi.setActive.mockRejectedValueOnce(new Error('disk full'))
    const wrapper = mount(KitPanel, { props: { championId: 103, position: 'MIDDLE' }, global })
    expect(wrapper.find('.badge').text()).toBe('Active')
    await wrapper.findAll('.u-button').find(button => button.text() === 'Used in champion select')!.trigger('click')
    await flushPromises()
    expect(kitsApi.setActive).toHaveBeenCalledWith(103, 'MIDDLE', null)
    expect(toasts.map(toast => toast.title)).toEqual(['Could not change the active kit'])
  })
})

describe('KitBuild', () => {
  it('draws the kit with its imports, the spells only in champion select', async () => {
    const wrapper = mount(KitBuild, { props: { kit: kit() }, global })
    expect(wrapper.get('.core').attributes('data-champion')).toBe('Ahri')
    await wrapper.get('[aria-label="Set these spells in champion select"]').trigger('click')
    await wrapper.get('[aria-label="Import items into the client"]').trigger('click')
    expect(kitImport.importSpells).toHaveBeenCalledWith(expect.objectContaining({ id: 'k1' }))
    expect(kitImport.importItems).toHaveBeenCalledWith(expect.objectContaining({ id: 'k1' }), 'Ahri')

    kitImport.inChampSelect.value = false
    kitImport.available.value = false
    const away = mount(KitBuild, { props: { kit: kit() }, global })
    expect(away.find('[aria-label="Set these spells in champion select"]').exists()).toBe(false)
    expect(away.find('[aria-label="Import items into the client"]').exists()).toBe(false)
  })
})
