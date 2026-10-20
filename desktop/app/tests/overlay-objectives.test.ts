import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import type { DrakeElement, GameState, NeutralTimers } from '../app/types/game'
import OverlayObjectives from '../app/components/overlay/OverlayObjectives.vue'
import { sampleGame } from '../app/utils/overlay-sample'

// The panel outside Nuxt: its auto-imports stubbed as globals.
const global = { stubs: { AppMark: true, UIcon: true } }
const NOW = 1_000_000

function game(timers: NeutralTimers | null, gameTime: number, drakes: [DrakeElement[], DrakeElement[]] = [[], []]): GameState {
  const base = sampleGame()
  return {
    ...base,
    gameTime,
    objectives: {
      order: { ...base.objectives.order, dragons: drakes[0].length, drakes: drakes[0] },
      chaos: { ...base.objectives.chaos, dragons: drakes[1].length, drakes: drakes[1] },
      timers,
    },
  }
}

const fresh: NeutralTimers = {
  dragon: { at: 300, until: null },
  elder: false,
  drake: null,
  grubs: { at: 480, until: 885 },
  herald: { at: 900, until: 1185 },
  baron: { at: 1200, until: null },
}
const GONE = { at: null, until: null }

const pill = (wrapper: ReturnType<typeof mount>, key: string) => wrapper.get(`[data-objective="${key}"]`)

beforeEach(() => {
  vi.useFakeTimers()
  vi.setSystemTime(NOW)
  for (const [name, value] of Object.entries({ ref, computed, onMounted, onBeforeUnmount })) vi.stubGlobal(name, value)
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

describe('OverlayObjectives', () => {
  it('counts every objective down from the clock under its icon alone, and runs it between readings', async () => {
    const wrapper = mount(OverlayObjectives, { props: { game: game(fresh, 240), syncedAt: NOW }, global })
    expect(pill(wrapper, 'dragon').get('[data-icon]').attributes('src')).toMatch(/\/dragon\.png$/)
    expect(pill(wrapper, 'dragon').text()).toBe('1:00')
    expect(pill(wrapper, 'grubs').text()).toBe('4:00')
    expect(pill(wrapper, 'herald').text()).toBe('11:00')
    expect(pill(wrapper, 'baron').text()).toBe('16:00')

    await vi.advanceTimersByTimeAsync(60_000)
    expect(pill(wrapper, 'dragon').attributes('data-status')).toBe('up')
    expect(pill(wrapper, 'dragon').find('[data-countdown]').exists()).toBe(false)
    // No drake taken yet: the pit is a row like the others, a green dot on the right.
    expect(pill(wrapper, 'dragon').find('[data-side]').exists()).toBe(false)
    expect(pill(wrapper, 'dragon').find('[data-up]').exists()).toBe(true)
    expect(pill(wrapper, 'grubs').text()).toBe('3:00')
  })

  it('flanks the Rift\'s drake with blue side\'s drakes on the left and red side\'s on the right, and hides the camps taken', () => {
    const timers: NeutralTimers = {
      ...fresh,
      dragon: { at: 1898, until: null },
      drake: 'mountain',
      grubs: GONE,
      herald: GONE,
      baron: { at: 1855, until: null },
    }
    const wrapper = mount(OverlayObjectives, { props: { game: game(timers, 1606, [['infernal', 'mountain', 'mountain'], ['ocean']]), syncedAt: NOW }, global })
    const icons = (side: string) => pill(wrapper, 'dragon').findAll(`[data-side="${side}"] img`).map(img => img.attributes('src')!.split('/').pop())
    expect(icons('blue')).toEqual(['dragon_infernal.png', 'dragon_mountain.png', 'dragon_mountain.png'])
    expect(icons('red')).toEqual(['dragon_ocean.png'])
    expect(pill(wrapper, 'dragon').get('[data-icon]').attributes('src')).toMatch(/dragon_mountain\.png$/)
    expect(pill(wrapper, 'dragon').get('[data-countdown]').text()).toBe('4:52')
    expect(wrapper.find('[data-objective="grubs"]').exists()).toBe(false)
    expect(wrapper.find('[data-objective="herald"]').exists()).toBe(false)
    expect(pill(wrapper, 'baron').find('[data-side]').exists()).toBe(false)
    expect(pill(wrapper, 'baron').text()).toBe('4:09')
  })

  it('shows the Elder\'s icon once a side holds the soul, and drops the camps that left the map', () => {
    const timers: NeutralTimers = { ...fresh, dragon: { at: 2110, until: null }, elder: true, drake: 'ocean' }
    const wrapper = mount(OverlayObjectives, { props: { game: game(timers, 2200, [['cloud'], ['ocean', 'hextech', 'ocean', 'ocean']]), syncedAt: NOW }, global })
    expect(pill(wrapper, 'dragon').get('[data-icon]').attributes('src')).toMatch(/dragon_elder\.png$/)
    expect(pill(wrapper, 'dragon').get('[data-icon]').attributes('alt')).toBe('Elder')
    expect(pill(wrapper, 'dragon').findAll('[data-side="red"] img')).toHaveLength(4)
    expect(wrapper.findAll('[data-objective]').map(row => row.attributes('data-objective'))).toEqual(['dragon', 'baron'])
    expect(pill(wrapper, 'baron').attributes('data-status')).toBe('up')
    expect(pill(wrapper, 'baron').find('[data-up]').exists()).toBe(true)
    // Between the drakes, a standing pit is its icon alone.
    expect(pill(wrapper, 'dragon').find('[data-up]').exists()).toBe(false)
  })

  it('says so off the Rift, where the shell has no timers', () => {
    const wrapper = mount(OverlayObjectives, { props: { game: game(null, 300), syncedAt: NOW }, global })
    expect(wrapper.find('[data-objective]').exists()).toBe(false)
    expect(wrapper.text()).toContain('standard rules')
  })
})
