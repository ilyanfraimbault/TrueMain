import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import type { GameState, NeutralTimers } from '../app/types/game'
import OverlayObjectives from '../app/components/overlay/OverlayObjectives.vue'
import { sampleGame } from '../app/utils/overlay-sample'

// The panel outside Nuxt: its auto-imports stubbed as globals.
const global = { stubs: { AppMark: true, UIcon: true } }
const NOW = 1_000_000

function game(timers: NeutralTimers | null, gameTime: number, dragons: [number, number] = [0, 0]): GameState {
  const base = sampleGame()
  return {
    ...base,
    gameTime,
    objectives: {
      order: { ...base.objectives.order, dragons: dragons[0], grubs: 0 },
      chaos: { ...base.objectives.chaos, dragons: dragons[1], grubs: 0 },
      timers,
    },
  }
}

const fresh: NeutralTimers = {
  dragon: { at: 300, until: null, takenBy: null },
  elder: false,
  grubs: { at: 480, until: 885, takenBy: null },
  herald: { at: 900, until: 1185, takenBy: null },
  baron: { at: 1200, until: null, takenBy: null },
}

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
  it('counts every objective down from the clock, and runs it between readings', async () => {
    const wrapper = mount(OverlayObjectives, { props: { game: game(fresh, 240), syncedAt: NOW }, global })
    expect(pill(wrapper, 'dragon').text()).toContain('Drake')
    expect(pill(wrapper, 'dragon').text()).toContain('1:00')
    expect(pill(wrapper, 'grubs').text()).toContain('4:00')
    expect(pill(wrapper, 'herald').text()).toContain('11:00')
    expect(pill(wrapper, 'baron').text()).toContain('16:00')

    await vi.advanceTimersByTimeAsync(60_000)
    expect(pill(wrapper, 'dragon').attributes('data-status')).toBe('up')
    expect(pill(wrapper, 'dragon').text()).toContain('Up')
    expect(pill(wrapper, 'grubs').text()).toContain('3:00')
  })

  it('shows the drakes each side holds, the soul point, and who took a camp', () => {
    const timers: NeutralTimers = {
      ...fresh,
      dragon: { at: 1898, until: null, takenBy: 'ORDER' },
      grubs: { at: null, until: null, takenBy: 'CHAOS' },
      herald: { at: null, until: null, takenBy: 'CHAOS' },
      baron: { at: 1855, until: null, takenBy: 'ORDER' },
    }
    const wrapper = mount(OverlayObjectives, { props: { game: game(timers, 1606, [3, 1]), syncedAt: NOW }, global })
    const drakes = wrapper.get('[data-drakes]').text()
    expect(drakes).toContain('3')
    expect(drakes).toContain('1')
    expect(drakes).toContain('Soul pt')
    expect(pill(wrapper, 'dragon').text()).toContain('4:52')
    expect(pill(wrapper, 'herald').text()).toContain('Taken')
    expect(pill(wrapper, 'herald').find('.text-enemy').exists()).toBe(true)
    expect(pill(wrapper, 'baron').text()).toContain('4:09')
  })

  it('names the Elder once a side holds the soul, and a camp gone untaken', () => {
    const timers: NeutralTimers = { ...fresh, dragon: { at: 2110, until: null, takenBy: 'CHAOS' }, elder: true }
    const wrapper = mount(OverlayObjectives, { props: { game: game(timers, 1900, [1, 4]), syncedAt: NOW }, global })
    expect(pill(wrapper, 'dragon').text()).toContain('Elder')
    expect(wrapper.get('[data-drakes]').text()).toContain('Soul')
    expect(pill(wrapper, 'grubs').text()).toContain('Gone')
    expect(pill(wrapper, 'herald').text()).toContain('Gone')
    expect(pill(wrapper, 'baron').text()).toContain('Up')
  })

  it('says so off the Rift, where the shell has no timers', () => {
    const wrapper = mount(OverlayObjectives, { props: { game: game(null, 300), syncedAt: NOW }, global })
    expect(wrapper.find('[data-objective]').exists()).toBe(false)
    expect(wrapper.text()).toContain('standard rules')
  })
})
