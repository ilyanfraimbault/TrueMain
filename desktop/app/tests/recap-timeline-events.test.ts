import type { Moment } from '../app/types/recordings'
import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed } from 'vue'
import RecapTimelineEvents from '../app/components/recap/RecapTimelineEvents.vue'
import { momentCast } from '../app/utils/recording-moments'
import { clusterMoments } from '../app/utils/recording-timeline'

// The events' row outside Nuxt: its auto-imports stubbed as globals, the glyph as a plain element.
const global = { stubs: { RecapMomentIcon: { props: ['kind'], template: '<i class="glyph" :data-kind="kind" />' } } }

const moment = (kind: Moment['kind'], videoMs: number, extra: Partial<Moment> = {}): Moment =>
  ({ kind, videoMs, endVideoMs: videoMs, kills: kind === 'kill' ? 1 : 0, ally: null, killerId: null, victimIds: [], dragon: null, ...extra })

const NAMES: Record<number, string> = { 103: 'Ahri', 238: 'Zed', 99: 'Lux' }
const cast = momentCast(103, { champions: [{ participantId: 3, championId: 103 }, { participantId: 7, championId: 238 }, { participantId: 8, championId: 99 }] })
const view = { startMs: 0, spanMs: 100_000 }

beforeEach(() => {
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('useChampionStatics', () => ({
    nameOf: (id: number) => NAMES[id] ?? String(id),
    portraitOf: (id: number) => `https://cdn/${NAMES[id]}.png`,
  }))
})

afterEach(() => vi.unstubAllGlobals())

describe('RecapTimelineEvents', () => {
  const clusters = clusterMoments([
    moment('kill', 40_000, { kills: 2, victimIds: [7, 8] }),
    moment('death', 41_000, { killerId: 7 }),
    moment('dragon', 70_000, { ally: false, dragon: 'ocean' }),
    moment('assist', 90_000),
  ], 2_000)

  it('draws a dot per cluster in the colour of the side it went, split when both', () => {
    const wrapper = mount(RecapTimelineEvents, { props: { clusters, view, hovered: null, cast }, global })
    const dots = wrapper.findAll('button')

    expect(dots).toHaveLength(3)
    expect(dots[0]!.classes().join(' ')).toContain('linear-gradient')
    expect(dots[0]!.attributes('aria-label')).toBe('Double kill at 0:40, Death at 0:41')
    expect(dots[1]!.classes()).toContain('bg-enemy')
    expect(dots[2]!.classes()).toContain('bg-ally')
    expect(wrapper.find('[role=tooltip]').exists()).toBe(false)
  })

  it('lists who killed whom in the hovered cluster\'s tooltip', () => {
    const wrapper = mount(RecapTimelineEvents, { props: { clusters, view, hovered: clusters[0]!, cast }, global })
    const tooltip = wrapper.get('[role=tooltip]')
    const portraits = tooltip.findAll('img').map(img => img.attributes('alt'))

    expect(tooltip.text()).toContain('Double kill')
    expect(tooltip.text()).toContain('Death')
    // The kill: Ahri over Zed and Lux; the death: Zed over Ahri.
    expect(portraits).toEqual(['Ahri', 'Zed', 'Lux', 'Zed', 'Ahri'])
    expect(tooltip.attributes('style')).toContain('translateX(-50%)')
  })

  it('draws an objective with the game\'s minimap icon and its drake\'s element', () => {
    const wrapper = mount(RecapTimelineEvents, { props: { clusters, view, hovered: clusters[1]!, cast }, global })
    const tooltip = wrapper.get('[role=tooltip]')

    expect(tooltip.text()).toContain('Enemy Ocean Drake')
    expect(tooltip.get('img').attributes('src')).toMatch(/\/dragon_ocean\.png$/)
  })

  it('falls back to the glyph when no champion can be named, and hangs from the dot near an edge', () => {
    const edge = clusterMoments([moment('assist', 95_000, { killerId: 4, victimIds: [9] })], 0)
    const wrapper = mount(RecapTimelineEvents, { props: { clusters: edge, view, hovered: edge[0]!, cast: momentCast(null, null) }, global })
    const tooltip = wrapper.get('[role=tooltip]')

    expect(tooltip.findAll('img')).toHaveLength(0)
    expect(tooltip.get('.glyph').attributes('data-kind')).toBe('assist')
    expect(tooltip.attributes('style')).toContain('calc(-100% + 12px)')

    const start = clusterMoments([moment('kill', 2_000)], 0)
    const left = mount(RecapTimelineEvents, { props: { clusters: start, view, hovered: start[0]!, cast }, global })
    expect(left.get('[role=tooltip]').attributes('style')).toContain('translateX(-12px)')
  })

  it('seeks to a dot\'s first event on a click', async () => {
    const wrapper = mount(RecapTimelineEvents, { props: { clusters, view, hovered: null, cast }, global })
    await wrapper.findAll('button')[0]!.trigger('click')

    expect(wrapper.emitted('moment')?.[0]?.[0]).toMatchObject({ kind: 'kill', videoMs: 40_000 })
  })
})
