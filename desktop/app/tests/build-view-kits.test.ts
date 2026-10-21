import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref, toRef, watch } from 'vue'
import BuildView from '../app/components/build/BuildView.vue'
import { kit } from './kit-fixtures'

/**
 * The build pane's kits (#2205): the player's kits on the champion and lane are
 * rows beside the builds, the draft's build stays the default, a kit picked
 * takes the pane, and a build picked goes back to the builds.
 */
const global = {
  stubs: {
    BuildList: { props: ['modelValue', 'options'], emits: ['update:modelValue'], template: '<div class="builds" :data-selected="modelValue"><button v-for="o in options" :key="o.key" class="build" @click="$emit(\'update:modelValue\', o.key)">{{ o.key }}</button></div>' },
    BuildMains: { emits: ['select'], template: '<button class="main" @click="$emit(\'select\', { identity: { gameName: \'x\' } })" />' },
    KitList: { props: ['kits', 'modelValue'], emits: ['update:modelValue'], template: '<div class="kits" :data-selected="modelValue"><button v-for="k in kits" :key="k.id" class="kit" @click="$emit(\'update:modelValue\', k.id)">{{ k.name }}</button></div>' },
    KitBuild: { props: ['kit'], template: '<div class="kit-build">{{ kit.name }}</div>' },
    BuildCore: { template: '<div class="core" />' },
    BuildTreeBlock: { template: '<div class="tree" />' },
    BuildMainBanner: true,
    UIcon: true,
  },
}

const lane = { builds: [{ firstItemId: 6655, primaryKeystoneId: 8112, core: { runePage: null }, buildTree: [], games: 10, winRate: 0.5 }] }

beforeEach(() => {
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('ref', ref)
  vi.stubGlobal('watch', watch)
  vi.stubGlobal('toRef', toRef)
  vi.stubGlobal('useChampionBuilds', () => ({ response: ref(lane), pending: ref(false) }))
  vi.stubGlobal('useStaticData', () => ({ items: ref({}), summoners: ref({}), runeTree: ref(null), pending: ref(false), loadChampion: () => {}, championStatic: () => null }))
  vi.stubGlobal('useChampionStatics', () => ({ patch: ref('16.21.1') }))
  vi.stubGlobal('useMainBuildOption', ({ selected }: { selected: { value: string | null } }) => ({
    mainNameTag: ref(null),
    onMain: computed(() => selected.value === 'main'),
    selectMain: () => (selected.value = 'main'),
    mainPending: ref(false),
    mainFailed: ref(false),
    mainDraft: ref(null),
    mainOption: ref(null),
    mainScope: ref(null),
  }))
})

afterEach(() => vi.unstubAllGlobals())

describe('BuildView with kits', () => {
  it('lists the kits beside the builds, the build first, and swaps the pane on a pick', async () => {
    const kits = [kit(), kit({ id: 'k2', name: 'Into mages' })]
    const wrapper = mount(BuildView, { props: { championId: 103, position: 'MIDDLE', kits, kitId: null, 'onUpdate:kitId': (id: string | null) => wrapper.setProps({ kitId: id }) }, global })
    expect(wrapper.text()).toContain('Your kits')
    expect(wrapper.findAll('.kit').map(button => button.text())).toEqual(['Into tanks', 'Into mages'])
    expect(wrapper.find('.core').exists()).toBe(true)
    expect(wrapper.find('.kit-build').exists()).toBe(false)

    await wrapper.findAll('.kit')[1]!.trigger('click')
    expect(wrapper.emitted('update:kitId')).toEqual([['k2']])
    expect(wrapper.get('.kit-build').text()).toBe('Into mages')
    expect(wrapper.get('.builds').attributes('data-selected')).toBeUndefined()

    await wrapper.get('.build').trigger('click')
    expect(wrapper.emitted('update:kitId')!.at(-1)).toEqual([null])
    expect(wrapper.find('.kit-build').exists()).toBe(false)

    await wrapper.findAll('.kit')[0]!.trigger('click')
    await wrapper.get('.main').trigger('click')
    expect(wrapper.emitted('update:kitId')!.at(-1)).toEqual([null])
  })

  it('has no kits section without kits', () => {
    const wrapper = mount(BuildView, { props: { championId: 103, position: 'MIDDLE' }, global })
    expect(wrapper.text()).not.toContain('Your kits')
  })
})
