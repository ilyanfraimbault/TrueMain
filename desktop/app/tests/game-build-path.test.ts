import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed } from 'vue'
import type { StaticItemData } from '#shared/types/static-data'
import type { BuildPath } from '../app/utils/build-path'
import GameBuildPath from '../app/components/game/GameBuildPath.vue'
import GameBuildPathTile from '../app/components/game/GameBuildPathTile.vue'

const statics: Record<number, StaticItemData> = {
  3070: { id: 3070, name: 'Tear of the Goddess', iconUrl: '', totalGold: 400 },
  3802: { id: 3802, name: 'Lost Chapter', iconUrl: '', totalGold: 1200 },
  1052: { id: 1052, name: 'Amplifying Tome', iconUrl: '', totalGold: 400 },
  2022: { id: 2022, name: 'Glowing Mote', iconUrl: '', totalGold: 250 },
}

const global = {
  stubs: {
    GameTooltipItemIcon: { props: ['item'], template: '<i class="icon" :data-item="item?.id" />' },
    UIcon: { template: '<i class="check" />' },
    GameBuildPathTile,
  },
}

const path: BuildPath = {
  measured: true,
  entries: [
    { itemId: 3070, cost: 0, state: 'owned', approx: false, share: 0.7, parts: [] },
    {
      itemId: 3802,
      cost: 950,
      state: 'buy',
      approx: false,
      share: 0.6,
      parts: [
        { itemId: 1052, cost: 400, state: 'buy', approx: true, share: 0.5 },
        { itemId: 2022, cost: 250, state: 'later', approx: false, share: 0.4 },
      ],
    },
  ],
}

beforeEach(() => vi.stubGlobal('computed', computed))
afterEach(() => vi.unstubAllGlobals())

describe('GameBuildPath', () => {
  it('draws each component in order, an opened intermediate as its parts under its icon', () => {
    const wrapper = mount(GameBuildPath, { props: { path, statics }, global })

    expect(wrapper.findAll('.icon').map(icon => icon.attributes('data-item'))).toEqual(['3070', '3802', '1052', '2022'])
    expect(wrapper.findAll('[aria-label$=": held"]')).toHaveLength(1)
    expect(wrapper.find('[aria-label="Amplifying Tome: about enough gold to buy now"]').text()).toBe('≈400')
    expect(wrapper.find('[aria-label="Glowing Mote: later"]').text()).toBe('250')
    expect(wrapper.find('[aria-label="Lost Chapter"]').exists()).toBe(true)
  })

  it('draws nothing for an item with no recipe', () => {
    const wrapper = mount(GameBuildPath, { props: { path: { measured: false, entries: [] }, statics }, global })
    expect(wrapper.find('ol').exists()).toBe(false)
  })

  it('checks an owned component and prices the one bought now', () => {
    const owned = mount(GameBuildPathTile, { props: { tile: path.entries[0]!, statics }, global })
    expect(owned.find('.check').exists()).toBe(true)

    const buy = mount(GameBuildPathTile, { props: { tile: { itemId: 9999, cost: 300, state: 'buy', approx: false, share: null }, statics }, global })
    expect(buy.attributes('aria-label')).toBe('Item 9999: buy now')
    expect(buy.text()).toBe('300')
  })
})
