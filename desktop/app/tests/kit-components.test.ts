import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref, toRef, watch } from 'vue'
import KitTree from '../app/components/kit/KitTree.vue'
import KitList from '../app/components/kit/KitList.vue'
import KitSpells from '../app/components/kit/KitSpells.vue'
import KitRunePage from '../app/components/kit/KitRunePage.vue'
import KitItemPicker from '../app/components/kit/KitItemPicker.vue'
import DraftBuildHeader from '../app/components/draft/DraftBuildHeader.vue'
import { item, kit, PAGE, RUNE_TREE } from './kit-fixtures'

// The kit editor's pieces outside Nuxt: auto-imports as globals, the site's
// icons and Nuxt UI as plain elements that keep what the tests read.
const global = {
  stubs: {
    GameTooltipItemIcon: { props: ['item'], template: '<img :alt="item?.name">' },
    GameTooltipPerkIcon: { props: ['perk'], template: '<img :alt="perk?.name">' },
    GameTooltipPerkStyleIcon: true,
    GameTooltipSummonerSpellIcon: true,
    ChampionPortrait: true,
    UIcon: true,
    UBadge: { props: ['label'], template: '<span class="badge">{{ label }}</span>' },
    UButton: { props: ['label'], emits: ['click'], template: '<button class="u-button" @click="$emit(\'click\')">{{ label }}</button>' },
    UPopover: { template: '<div><slot /><slot name="content" /></div>' },
    UModal: { props: ['open', 'title'], template: '<div v-if="open" class="modal"><h2>{{ title }}</h2><slot name="body" /></div>' },
    UInput: { props: ['modelValue'], emits: ['update:modelValue'], template: '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)">' },
  },
}

const items = { 6655: item(6655, { name: 'Luden\'s Echo' }), 3100: item(3100, { name: 'Lich Bane' }), 3089: item(3089, { name: 'Rabadon\'s Deathcap' }), 4645: item(4645, { name: 'Shadowflame' }) }

beforeEach(() => {
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('ref', ref)
  vi.stubGlobal('watch', watch)
  vi.stubGlobal('toRef', toRef)
  vi.stubGlobal('useStaticData', () => ({ items: ref(items), summoners: ref({ 4: { id: 4, name: 'Flash', iconUrl: '' }, 14: { id: 14, name: 'Ignite', iconUrl: '' } }), runeTree: ref(RUNE_TREE) }))
  vi.stubGlobal('useChampionStatics', () => ({ nameOf: () => 'Ahri' }))
})

afterEach(() => vi.unstubAllGlobals())

describe('KitTree', () => {
  it('asks for an item to replace a node, to branch after it, or as a new first item', async () => {
    const wrapper = mount(KitTree, { props: { tree: kit().tree, items, boots: 3020, editable: true }, global })
    await wrapper.get('[aria-label="Replace Shadowflame"]').trigger('click')
    await wrapper.get('[aria-label="Add another branch after Lich Bane"]').trigger('click')
    await wrapper.get('[aria-label="Add an item after Rabadon\'s Deathcap"]').trigger('click')
    await wrapper.get('[aria-label="Add another first item"]').trigger('click')
    await wrapper.get('[aria-label="Remove Lich Bane and what follows"]').trigger('click')
    expect(wrapper.emitted('pick')).toEqual([
      [{ at: [0, 1], mode: 'replace' }],
      [{ at: [0, 0], mode: 'child' }],
      [{ at: [0, 0, 0], mode: 'child' }],
      [{ at: [], mode: 'child' }],
    ])
    expect(wrapper.emitted('remove')).toEqual([[[0, 0]]])
  })

  it('offers no branch past a full path, and only the first item on an empty tree', () => {
    const chain = (ids: number[]) => ids.reduceRight<{ itemId: number, children: never[] }[]>((children, itemId) => [{ itemId, children: children as never[] }], [])
    const full = mount(KitTree, { props: { tree: chain([1, 2, 3, 4, 5]), items, boots: 3020, editable: true }, global })
    expect(full.findAll('[aria-label^="Add an item after"]')).toHaveLength(0)
    expect(full.findAll('[aria-label^="Add another branch after"]')).toHaveLength(4)
    const empty = mount(KitTree, { props: { tree: [], items, boots: null, editable: true }, global })
    expect(empty.find('[aria-label="Add the first item"]').exists()).toBe(true)
  })

  it('draws the tree read-only', () => {
    const wrapper = mount(KitTree, { props: { tree: kit().tree, items, boots: 3020 }, global })
    expect(wrapper.findAll('button')).toHaveLength(0)
    expect(wrapper.findAll('img').map(img => img.attributes('alt'))).toEqual(['Luden\'s Echo', 'Lich Bane', 'Rabadon\'s Deathcap', 'Shadowflame'])
    expect(wrapper.findAll('path')).toHaveLength(3)
  })
})

describe('KitList', () => {
  it('lists the kits with the active one marked, and picks one on a click', async () => {
    const kits = [kit(), kit({ id: 'k2', name: 'Into mages' })]
    const wrapper = mount(KitList, { props: { kits, activeId: 'k2', modelValue: 'k1' }, global })
    expect(wrapper.findAll('li').map(li => li.text())).toEqual(['Into tanks', 'Into magesActive'])
    await wrapper.findAll('li button')[1]!.trigger('click')
    expect(wrapper.emitted('update:modelValue')).toEqual([['k2']])
    expect(wrapper.get('[aria-current="true"]').text()).toContain('Into mages')
  })
})

describe('KitSpells', () => {
  it('swaps the two when a slot takes the other\'s spell', async () => {
    const wrapper = mount(KitSpells, { props: { modelValue: [4, 14], summoners: {} }, global })
    const options = (slot: number) => wrapper.findAll(`[aria-label="Spell ${slot}"] button`)
    await options(1).find(option => option.attributes('aria-label') === 'Spell 14')!.trigger('click')
    expect(wrapper.emitted('update:modelValue')).toEqual([[[14, 4]]])
    await wrapper.setProps({ modelValue: [4, 14] })
    await options(2).find(option => option.attributes('aria-label') === 'Spell 12')!.trigger('click')
    expect(wrapper.emitted('update:modelValue')![1]).toEqual([[4, 12]])
  })
})

describe('KitRunePage', () => {
  it('edits the page through its runes, trees and shards', async () => {
    const wrapper = mount(KitRunePage, {
      props: { 'modelValue': PAGE, 'tree': RUNE_TREE, 'onUpdate:modelValue': (page: typeof PAGE) => wrapper.setProps({ modelValue: page }) },
      global,
    })
    const updates = () => wrapper.emitted('update:modelValue')!.map(([page]) => page)
    await wrapper.get('[aria-label="Dark Harvest"]').trigger('click')
    expect(updates()[0]).toMatchObject({ primaryKeystoneId: 8128 })

    await wrapper.findAll('[role="group"][aria-label="Primary tree"] button')[2]!.trigger('click')
    expect(updates()[1]).toMatchObject({ primaryStyleId: 8000, secondaryStyleId: 8200 })
    await wrapper.findAll('[role="group"][aria-label="Secondary tree"] button')[0]!.trigger('click')
    expect(updates()[2]).toMatchObject({ secondaryStyleId: 8100, secondaryPerk1Id: 8126 })

    await wrapper.findAll('section[aria-label="Primary tree"] > div:not([role]) button')[3]!.trigger('click')
    expect(updates()[3]).toMatchObject({ primaryPerk1Id: 9111 })
    await wrapper.findAll('section[aria-label="Secondary tree"] > div:not([role]) button')[5]!.trigger('click')
    expect(updates()[4]).toMatchObject({ secondaryPerk1Id: 8126, secondaryPerk2Id: 8138 })
    await wrapper.findAll('[aria-label="Shards"] button')[4]!.trigger('click')
    expect(updates()).toHaveLength(6)
    expect(updates()[5]).toMatchObject({ statFlex: 5010 })
  })
})

describe('KitItemPicker', () => {
  it('searches the shop, refuses an item already on the path, and closes on a pick', async () => {
    const shop = { ...items, 1052: item(1052, { name: 'Amplifying Tome', totalGold: 400 }), 3802: item(3802, { name: 'Lost Chapter', from: [1052] }) }
    const wrapper = mount(KitItemPicker, { props: { open: true, items: shop, groups: ['legendary', 'component'], blocked: [6655], title: 'Add an item' }, global })
    expect(wrapper.findAll('h3').map(h => h.text())).toEqual(['Legendary', 'Components'])
    expect(wrapper.get('[aria-label="Luden\'s Echo"]').attributes('disabled')).toBeDefined()

    await wrapper.get('input').setValue('nothing at all')
    expect(wrapper.text()).toContain('No item matches')
    await wrapper.get('input').setValue('lich')
    expect(wrapper.findAll('section button').map(button => button.attributes('aria-label'))).toEqual(['Lich Bane'])
    await wrapper.get('[aria-label="Lich Bane"]').trigger('click')
    expect(wrapper.emitted('pick')).toEqual([[3100]])
    expect(wrapper.emitted('update:open')).toEqual([[false]])
  })
})

describe('DraftBuildHeader', () => {
  it('names the active kit and switches back to TrueMain\'s build', async () => {
    const wrapper = mount(DraftBuildHeader, { props: { championId: 103, position: 'MIDDLE', canGoBack: false, kitName: 'Into tanks', onKit: true }, global })
    expect(wrapper.text()).toContain('Your kit: Into tanks')
    await wrapper.get('.u-button').trigger('click')
    expect(wrapper.emitted('update:onKit')).toEqual([[false]])
    const plain = mount(DraftBuildHeader, { props: { championId: 103, position: 'MIDDLE', canGoBack: true }, global })
    expect(plain.text()).not.toContain('Your kit')
    await plain.get('.u-button').trigger('click')
    expect(plain.emitted('back')).toHaveLength(1)
  })
})
