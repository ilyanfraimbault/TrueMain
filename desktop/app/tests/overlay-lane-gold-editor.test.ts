import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, defineComponent, h, onBeforeUnmount, onMounted, reactive, ref, useTemplateRef } from 'vue'
import type { OverlaySettings } from '../app/types/overlay'
import OverlayGoldPlate from '../app/components/overlay/OverlayGoldPlate.vue'
import OverlayItemValue from '../app/components/overlay/OverlayItemValue.vue'
import OverlayLaneGold from '../app/components/overlay/OverlayLaneGold.vue'
import OverlayLayoutEditor from '../app/components/overlay/OverlayLayoutEditor.vue'
import OverlayPanelMock from '../app/components/overlay/OverlayPanelMock.vue'
import OverlayPanelSample from '../app/components/overlay/OverlayPanelSample.vue'
import OverlayScreenHud from '../app/components/overlay/OverlayScreenHud.vue'
import { DEV_OVERLAY_VIEW } from '../app/types/overlay'
import { sampleGame } from '../app/utils/overlay-sample'

// The Overlay page's pieces outside Nuxt: auto-imports stubbed as globals,
// the panels the lane gold does not concern stubbed as components.
const props = (name: string, ...keys: string[]) => defineComponent({ name, props: keys, setup: () => () => h('div', { 'data-stub': name }) })
const global = {
  stubs: { UIcon: true, OverlayHiddenPanels: true, OverlayNextItemCard: true, GameBuildOrder: true, OverlayWinProbability: true, OverlayStats: true, OverlayObjectives: true },
  components: { OverlayGoldPlate, OverlayItemValue, OverlayLaneGold, OverlayPanelMock, OverlayPanelSample, OverlayScreenHud, OverlayGoldGap: props('OverlayGoldGap', 'ours', 'theirs', 'large') },
}

function settings(change: Partial<OverlaySettings> = {}): OverlaySettings {
  return { ...DEV_OVERLAY_VIEW.settings, ...change }
}

beforeEach(() => {
  for (const [name, value] of Object.entries({ ref, computed, reactive, onMounted, onBeforeUnmount, useTemplateRef })) vi.stubGlobal(name, value)
  vi.stubGlobal('useStaticData', () => ({ items: ref({}) }))
  vi.stubGlobal('useChampionStatics', () => ({ champions: ref(new Map()), portraitOf: () => null, patch: ref('') }))
})
afterEach(() => vi.unstubAllGlobals())

describe('the lane gold on the Overlay page', () => {
  const plates = (wrapper: ReturnType<typeof mount>) => wrapper.findAll('[role="button"]').find(box => box.attributes('aria-label')!.startsWith('Lane gold'))!

  it('outlines the scoreboard and sits the plates on it, not to be dragged', async () => {
    const wrapper = mount(OverlayLayoutEditor, { props: { settings: settings(), hud: { globalScale: 0.3, mirrored: false, own: true } }, global })
    expect(wrapper.find('[data-scoreboard]').exists()).toBe(true)
    const box = plates(wrapper)
    expect(box.attributes('aria-label')).toBe('Lane gold: on the scoreboard, Delete to remove')
    expect(box.classes()).toContain('cursor-default')
    // Its mock is the rows' height, five plates, no card.
    expect(box.findAll('[data-row]')).toHaveLength(5)

    await box.trigger('keydown', { key: 'ArrowDown' })
    expect(wrapper.emitted('place')).toBeUndefined()
    await box.trigger('pointerdown', { button: 0 })
    await box.trigger('keydown', { key: 'Delete' })
    expect(wrapper.emitted('hide')).toEqual([['lane-gold']])
  })

  it('draws the item value without its lanes while the plates are on, and with them once off', () => {
    const lanes = (on: boolean) => {
      const wrapper = mount(OverlayLayoutEditor, { props: { settings: settings({ laneGold: { ...settings().laneGold, enabled: on } }) }, global })
      return wrapper.findAll('[role="button"]').find(box => box.attributes('aria-label')!.startsWith('Item value'))!.findAll('li').length
    }
    expect(lanes(true)).toBe(0)
    expect(lanes(false)).toBe(5)
  })

  it('leaves the scoreboard off the screen copy while the plates are off', () => {
    const wrapper = mount(OverlayLayoutEditor, { props: { settings: settings({ laneGold: { ...settings().laneGold, enabled: false } }) }, global })
    expect(wrapper.find('[data-scoreboard]').exists()).toBe(false)
    expect(plates(wrapper)).toBeUndefined()
  })

  it('draws the plates mock at the height it is given', () => {
    const wrapper = mount(OverlayPanelMock, { props: { panel: 'lane-gold', zoom: 0.5, height: 332, chord: 'Tab' }, global })
    expect(wrapper.html()).toContain('height: 332px')
    // The chord would sit on the last plate: left to the trigger picker.
    expect(wrapper.text()).not.toContain('Tab')
    expect(wrapper.findAll('[data-ahead]')).toHaveLength(5)
  })

  it('keeps the item value\'s lanes by default', () => {
    const wrapper = mount(OverlayItemValue, { props: { game: sampleGame() }, global })
    expect(wrapper.findAll('li')).toHaveLength(5)
  })
})
