import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref } from 'vue'
import type { StaticItemData } from '#shared/types/static-data'
import type { GamePlayer, GameState, GameTeam } from '../app/types/game'
import OverlayGoldPlate from '../app/components/overlay/OverlayGoldPlate.vue'
import OverlayLaneGold from '../app/components/overlay/OverlayLaneGold.vue'
import OverlayScoreboardPreview from '../app/components/overlay/OverlayScoreboardPreview.vue'
import OverlayScreenHud from '../app/components/overlay/OverlayScreenHud.vue'
import { lanePlates, plateGold, scoreboardPairs } from '../app/utils/lane-gold'
import { sampleGame } from '../app/utils/overlay-sample'

/** Items worth 100, 1,000 and 3,000 gold, and a potion, which never counts. */
const ITEMS: Record<number, StaticItemData> = {
  1: { id: 1, name: 'Small', iconUrl: '', totalGold: 100 },
  2: { id: 2, name: 'Medium', iconUrl: '', totalGold: 1000 },
  3: { id: 3, name: 'Large', iconUrl: '', totalGold: 3000 },
  4: { id: 4, name: 'Potion', iconUrl: '', totalGold: 50, tags: ['Consumable'] },
} as Record<number, StaticItemData>

const template = sampleGame().players[0]!
const player = (riotId: string, team: GameTeam, position: string, items: number[]): GamePlayer => ({
  ...template,
  riotId,
  team,
  position,
  items: items.map((itemId, slot) => ({ itemId, slot, count: 1 })),
})

/** Listed red side first and out of lane order, as the API may. */
function game(myTeam: GameTeam | null = 'ORDER'): GameState {
  return {
    ...sampleGame(),
    myTeam,
    players: [
      player('red-mid', 'CHAOS', 'MIDDLE', [3]),
      player('red-top', 'CHAOS', 'TOP', [2, 2]),
      player('blue-sup', 'ORDER', 'UTILITY', [1, 4]),
      player('blue-top', 'ORDER', 'TOP', [3]),
      player('red-jg', 'CHAOS', 'JUNGLE', [2]),
      player('blue-mid', 'ORDER', 'MIDDLE', [3, 1]),
      player('blue-jg', 'ORDER', 'JUNGLE', [2]),
      player('blue-bot', 'ORDER', 'BOTTOM', [2]),
      player('red-bot', 'CHAOS', 'BOTTOM', [3, 3]),
      player('red-sup', 'CHAOS', 'UTILITY', [1, 1]),
    ],
  }
}

describe('the scoreboard\'s rows', () => {
  it('puts blue side on the left and red on the right, each in lane order, whichever side we are on', () => {
    for (const side of ['ORDER', 'CHAOS', null] as const) {
      const rows = scoreboardPairs(game(side)).map(({ left, right }) => [left?.riotId, right?.riotId])
      expect(rows).toEqual([
        ['blue-top', 'red-top'],
        ['blue-jg', 'red-jg'],
        ['blue-mid', 'red-mid'],
        ['blue-bot', 'red-bot'],
        ['blue-sup', 'red-sup'],
      ])
    }
  })

  it('keeps the API\'s order in a queue without lanes, and leaves a row without its pair empty', () => {
    const blind: GameState = {
      ...game(),
      players: [player('b1', 'ORDER', '', [1]), player('r1', 'CHAOS', '', [2]), player('b2', 'ORDER', '', [3])],
    }
    expect(scoreboardPairs(blind).map(({ left, right }) => [left?.riotId ?? null, right?.riotId ?? null])).toEqual([['b1', 'r1'], ['b2', null]])
    expect(lanePlates(blind, ITEMS)).toEqual([{ gold: 900, ahead: 'right', ours: false }, null])
  })
})

describe('the lane-gold plates', () => {
  it('shows each row\'s gap toward the side ahead, coloured ours or theirs', () => {
    expect(lanePlates(game('ORDER'), ITEMS)).toEqual([
      { gold: 1000, ahead: 'left', ours: true },
      { gold: 0, ahead: null, ours: false },
      { gold: 100, ahead: 'left', ours: true },
      { gold: 5000, ahead: 'right', ours: false },
      // The potion counts nothing: 100 against 200.
      { gold: 100, ahead: 'right', ours: false },
    ])
  })

  it('keeps the arrows on the scoreboard\'s sides and swaps the colours when we are red side', () => {
    const plates = lanePlates(game('CHAOS'), ITEMS)
    expect(plates[0]).toEqual({ gold: 1000, ahead: 'left', ours: false })
    expect(plates[3]).toEqual({ gold: 5000, ahead: 'right', ours: true })
  })

  it('prints a gap short', () => {
    expect(plateGold(862)).toBe('862')
    expect(plateGold(1500)).toBe('1.5k')
    expect(plateGold(1449)).toBe('1.4k')
    expect(plateGold(12_400)).toBe('12k')
  })
})

describe('OverlayLaneGold', () => {
  const global = { stubs: { UIcon: true }, components: { OverlayGoldPlate, OverlayLaneGold, OverlayScreenHud } }

  beforeEach(() => {
    vi.stubGlobal('computed', computed)
    vi.stubGlobal('useStaticData', () => ({ items: ref(ITEMS) }))
  })
  afterEach(() => vi.unstubAllGlobals())

  it('draws a plate centred on each of the five rows', () => {
    const wrapper = mount(OverlayLaneGold, { props: { game: game() }, global })
    const rows = wrapper.findAll('[data-row]')
    expect(rows).toHaveLength(5)
    expect(rows.map(row => row.find('[data-ahead]').attributes('data-ahead'))).toEqual(['left', 'even', 'left', 'right', 'right'])
    expect(rows[0]!.text()).toBe('1k')
    expect(rows[0]!.find('[data-ahead]').classes()).toContain('text-ally')
    expect(rows[3]!.text()).toBe('5k')
    expect(rows[3]!.find('[data-ahead]').classes()).toContain('text-enemy')
  })

  it('leaves a row without its pair empty', () => {
    const short: GameState = { ...game(), players: game().players.filter(p => p.riotId !== 'red-sup') }
    const wrapper = mount(OverlayLaneGold, { props: { game: short }, global })
    expect(wrapper.findAll('[data-row]')).toHaveLength(5)
    expect(wrapper.find('[data-row="4"] [data-ahead]').exists()).toBe(false)
  })

  it('sits the plates on the scoreboard\'s outline in the simulator\'s screen', () => {
    const wrapper = mount(OverlayScoreboardPreview, { props: { game: game() }, global })
    expect(wrapper.find('[data-scoreboard]').exists()).toBe(true)
    // League's default HUD scale on 1080p: rows from 360.8 to 692.8.
    const style = wrapper.get('[data-plates]').attributes('style')
    expect(style).toContain('top: 33.4')
    expect(style).toContain('height: 30.7')
    expect(wrapper.findAll('[data-ahead]')).toHaveLength(5)
  })
})
