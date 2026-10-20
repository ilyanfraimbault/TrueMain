import type { Moment } from '../app/types/recordings'
import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue'
import RecapTimeline from '../app/components/recap/RecapTimeline.vue'
import RecapTimelineEvents from '../app/components/recap/RecapTimelineEvents.vue'
import { fullView, isInView, viewPercent } from '../app/utils/recording-timeline'

// The timeline outside Nuxt: a 1000 px track over a 100 s video, so 1 px is 100 ms
// and a dot's width (10 px) is a second. The zoom is held at the whole video.
const LENGTH = 100_000
const WIDTH = 1000

const global = {
  components: { RecapTimelineEvents },
  stubs: {
    RecapTimelineLegend: true,
    RecapTimelineClock: true,
    RecapTimelineNavigator: true,
    RecapWinProbabilityLane: true,
    RecapMomentIcon: { props: ['kind'], template: '<i />' },
  },
}

const moment = (kind: Moment['kind'], videoMs: number, extra: Partial<Moment> = {}): Moment =>
  ({ kind, videoMs, endVideoMs: videoMs, kills: kind === 'kill' ? 1 : 0, ally: null, killerId: null, victimIds: [], dragon: null, ...extra })

const MOMENTS = [
  moment('kill', 20_000),
  moment('assist', 20_500),
  moment('death', 60_000),
  moment('baron', 99_000, { ally: true }),
  // Past the video's end: never drawn.
  moment('tower', 120_000, { ally: true }),
]

beforeEach(() => {
  vi.stubGlobal('ref', ref)
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('onMounted', onMounted)
  vi.stubGlobal('onBeforeUnmount', onBeforeUnmount)
  vi.stubGlobal('useChampionStatics', () => ({ nameOf: String, portraitOf: () => null }))
  vi.stubGlobal('useTimelineZoom', () => {
    const view = ref(fullView(LENGTH))
    return {
      view,
      zoomed: computed(() => false),
      pct: (ms: number) => viewPercent(view.value, ms),
      inView: (ms: number) => isInView(view.value, ms),
      msAt: (clientX: number) => (clientX / WIDTH) * LENGTH,
      setView: vi.fn(),
      zoomStep: vi.fn(),
      onWheel: vi.fn(),
      scrollPastEdge: vi.fn(),
    }
  })
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockReturnValue({ width: WIDTH, left: 0 } as DOMRect)
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

const mountTimeline = (props: Record<string, unknown> = {}) =>
  mount(RecapTimeline, { props: { lengthMs: LENGTH, currentMs: 0, moments: MOMENTS, ...props }, global })

describe('RecapTimeline', () => {
  it('merges the events a dot\'s width apart and leaves out those past the video', async () => {
    const wrapper = mountTimeline()
    // The track's width is read once mounted; the dots merge from then on.
    await nextTick()
    const dots = wrapper.findAll('button[aria-label]')

    expect(dots.map(dot => dot.attributes('aria-label'))).toEqual([
      'Kill at 0:20, Assist at 0:20',
      'Death at 1:00',
      'Baron at 1:39',
    ])
  })

  it('opens the tooltip of the dot under the pointer, wherever it is on the track', async () => {
    const wrapper = mountTimeline()
    const track = wrapper.get('.touch-none')

    await track.trigger('pointermove', { clientX: 603 })
    expect(wrapper.get('[role=tooltip]').text()).toContain('Death')

    // Nothing within a dot's width: no tooltip.
    await track.trigger('pointermove', { clientX: 400 })
    expect(wrapper.find('[role=tooltip]').exists()).toBe(false)

    await track.trigger('pointermove', { clientX: 600 })
    await track.trigger('pointerleave')
    expect(wrapper.find('[role=tooltip]').exists()).toBe(false)
  })

  it('jumps to a dot\'s first event and seeks a click on the bare track', async () => {
    const wrapper = mountTimeline()
    await nextTick()
    await wrapper.findAll('button[aria-label]')[2]!.trigger('click')
    expect(wrapper.emitted('moment')?.[0]?.[0]).toMatchObject({ kind: 'baron' })

    const track = wrapper.get('.touch-none')
    track.element.setPointerCapture = vi.fn()
    await track.trigger('pointerdown', { button: 0, clientX: 300 })
    await track.trigger('pointerup', { clientX: 300 })
    expect(wrapper.emitted('seek')?.[0]).toEqual([30_000])
  })

  it('cuts a range when editable, the curve\'s lane taking the panel', async () => {
    const wrapper = mountTimeline({ editable: true, winProbability: { curve: [], turningPoints: [] } })
    await nextTick()
    expect(wrapper.find('recap-win-probability-lane-stub').exists()).toBe(true)

    const track = wrapper.get('.touch-none')
    track.element.setPointerCapture = vi.fn()
    await track.trigger('pointerdown', { button: 0, clientX: 100 })
    await track.trigger('pointermove', { clientX: 200 })
    // A drag in progress hides the tooltip.
    expect(wrapper.find('[role=tooltip]').exists()).toBe(false)
    await track.trigger('pointerup', { clientX: 200 })
    expect(wrapper.emitted('create')?.[0]).toEqual([10_000, 20_000])
  })
})
