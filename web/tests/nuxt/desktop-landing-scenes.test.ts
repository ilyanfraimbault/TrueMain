import { mountSuspended } from '@nuxt/test-utils/runtime'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import {
  DesktopLandingDraftDemo,
  DesktopLandingGameScreen,
  DesktopLandingInGame,
  DesktopLandingOverlayPanel,
  DesktopLandingRecapDemo,
} from '#components'
import type { StaticItemData } from '#shared/types/static-data'
import { sampleWinProbability } from '~/utils/desktop-landing-sample'

// `/download`'s drawn scenes (#2072). Each is server-rendered — and drawn for a
// visitor who asked for less motion — in its final state, so these pin that
// state: the values the app's own overlay sample produces, every panel on, the
// clip saved, Ahri hovered.

const item = (name: string, totalGold: number, tags: string[] = []): StaticItemData => ({ name, iconUrl: '', totalGold, tags } as unknown as StaticItemData)

/** The sample's items at round prices, so the lane sums are checkable by hand. */
const ITEMS: Record<number, StaticItemData> = {
  3031: item('Infinity Edge', 3450),
  1018: item('Cloak of Agility', 600),
  3047: item('Plated Steelcaps', 1200),
  3190: item('Locket of the Iron Solari', 2200),
}

const PATCH = '16.20.1'

/** The scenes only play once a viewport says they are on screen; without one they stay in their final state. */
beforeEach(() => {
  vi.stubGlobal('IntersectionObserver', undefined)
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('LandingOverlayPanel', () => {
  it('shows the next item of the sample with the gold it still needs', async () => {
    const wrapper = await mountSuspended(DesktopLandingOverlayPanel, { props: { panel: 'next-item', patch: PATCH, items: ITEMS } })

    expect(wrapper.text()).toContain('Infinity Edge')
    expect(wrapper.text()).toContain('650 gold to go')
    expect(wrapper.find('img').attributes('src')).toContain('3031.png')
    expect(wrapper.attributes('style')).toContain('width: 232px')
  })

  it('draws the build row under it: done dimmed, the next one ringed, the rest to come', async () => {
    const wrapper = await mountSuspended(DesktopLandingOverlayPanel, { props: { panel: 'next-item', patch: PATCH, items: ITEMS } })

    const row = wrapper.findAll('[data-build-row] img')
    expect(row.map(img => img.attributes('src')?.split('/').pop())).toEqual(['6672.png', '3006.png', '3031.png', '3036.png', '3072.png', '3026.png'])
    expect(row[0]!.classes()).toContain('grayscale')
    expect(row[2]!.classes()).toContain('ring-primary')
    expect(row[3]!.classes()).toContain('opacity-80')
  })

  it('names the item before the item map is read, and draws an empty slot for its icon', async () => {
    const wrapper = await mountSuspended(DesktopLandingOverlayPanel, { props: { panel: 'next-item', patch: null, items: null } })

    expect(wrapper.text()).toContain('Infinity Edge')
    expect(wrapper.find('img').exists()).toBe(false)
  })

  it('splits the win probability the shared model gives the sample', async () => {
    const wrapper = await mountSuspended(DesktopLandingOverlayPanel, { props: { panel: 'win-probability', patch: PATCH, items: null } })
    const ours = Math.round(sampleWinProbability() * 100)

    expect(ours).toBeGreaterThan(50)
    expect(wrapper.text()).toContain(`${ours}%`)
    expect(wrapper.text()).toContain(`${100 - ours}%`)
  })

  it('draws the pace of the frame\'s 13th minute against the Diamond median', async () => {
    const wrapper = await mountSuspended(DesktopLandingOverlayPanel, { props: { panel: 'pace', patch: PATCH, items: null } })

    expect(wrapper.text()).toContain('7.6')
    expect(wrapper.text()).toContain('Diamond 7.1')
    expect(wrapper.text()).toContain('440')
    expect(wrapper.text()).toContain('Diamond 432')
    expect(wrapper.findAll('polyline')).toHaveLength(2)
    // The curve ends on a dot, as OverlayStats draws it.
    expect(wrapper.findAll('circle')).toHaveLength(1)
  })

  it('sums item gold lane by lane, leaving out consumables and trinkets', async () => {
    const items = { ...ITEMS, 3047: item('Plated Steelcaps', 1200, ['Consumable']) }
    const wrapper = await mountSuspended(DesktopLandingOverlayPanel, { props: { panel: 'item-value', patch: PATCH, items } })
    const rows = wrapper.findAll('li')

    expect(rows).toHaveLength(5)
    // Bottom lane: Jinx holds an Infinity Edge (3,450) and a Cloak of Agility (600), Ezreal nothing priced here.
    expect(rows[3]!.text()).toContain('4,050')
    // Support: Locket on both sides, the boots counted as a consumable for this test.
    expect(rows[4]!.text()).toContain('2,200')
    expect(wrapper.findAll('img')).toHaveLength(10)
  })

  it('shows dashes until the item map is read', async () => {
    const wrapper = await mountSuspended(DesktopLandingOverlayPanel, { props: { panel: 'item-value', patch: null, items: null } })

    expect(wrapper.text()).toContain('–')
    expect(wrapper.findAll('img')).toHaveLength(0)
  })
})

describe('LandingGameScreen', () => {
  it('turns on the panels it is given, item value only while TAB is held', async () => {
    const wrapper = await mountSuspended(DesktopLandingGameScreen, {
      props: { shown: ['win-probability', 'item-value'], tabHeld: false, patch: PATCH, items: ITEMS },
    })

    expect(wrapper.findAll('.landing-panel.is-on')).toHaveLength(1)

    await wrapper.setProps({ tabHeld: true })
    expect(wrapper.findAll('.landing-panel.is-on')).toHaveLength(2)
    expect(wrapper.find('kbd').classes()).toContain('opacity-100')
  })
})

describe('LandingInGame', () => {
  it('lists the four panels and draws them all when the steps cannot drive the screen', async () => {
    const wrapper = await mountSuspended(DesktopLandingInGame, { props: { patch: PATCH, items: ITEMS } })

    expect(wrapper.findAll('ol > li')).toHaveLength(4)
    expect(wrapper.text()).toContain('What to buy next')
    expect(wrapper.findAll('.landing-panel.is-on')).toHaveLength(4)
  })
})

describe('LandingDraftDemo', () => {
  it('ends on Ahri hovered from the app, the lock button taking her', async () => {
    const wrapper = await mountSuspended(DesktopLandingDraftDemo, { props: { patch: PATCH } })

    expect(wrapper.text()).toContain('Lock in Ahri')
    expect(wrapper.text()).toContain('Hovered')
    expect(wrapper.text()).not.toContain('Runes')
    expect(wrapper.findAll('.landing-rise.is-on')).toHaveLength(4)
  })

  it('draws no champion icon until the patch is known', async () => {
    const wrapper = await mountSuspended(DesktopLandingDraftDemo, { props: { patch: null } })

    expect(wrapper.findAll('img').filter(img => img.attributes('src')?.includes('champion/'))).toHaveLength(0)
  })
})

describe('LandingRecapDemo', () => {
  it('ends on the triple kill saved and its clip at the head of the list', async () => {
    const wrapper = await mountSuspended(DesktopLandingRecapDemo, { props: { patch: '15.1.1' } })

    expect(wrapper.text()).toContain('Saved clips · 3')
    expect(wrapper.text()).toContain('Triple kill on Yone')
    expect(wrapper.text()).toContain('13:23 / 31:40')
    // Once saved, the range being cut is gone from the side panel.
    expect(wrapper.text()).not.toContain('12:48 → 13:33')
    expect(wrapper.find('img').attributes('src')).toBe('/desktop/game.webp')
  })

  it('opens the triple kill\'s tooltip: who killed whom, and the drake beside it (#2171)', async () => {
    const wrapper = await mountSuspended(DesktopLandingRecapDemo, { props: { patch: '15.1.1' } })
    const sources = wrapper.findAll('img').map(img => img.attributes('src') ?? '')

    expect(wrapper.text()).toContain('Triple kill13:15')
    expect(wrapper.text()).toContain('Infernal Drake13:40')
    for (const champion of ['Yone', 'Orianna', 'Ezreal', 'Nautilus']) expect(sources.some(src => src.includes(`/${champion}.png`))).toBe(true)
    expect(sources.some(src => src.endsWith('/dragon_infernal.png'))).toBe(true)
  })
})

/** An observer the test drives: every scene on screen at once, motion allowed. */
class OnScreenObserver {
  constructor(readonly callback: (entries: { isIntersecting: boolean }[]) => void) {}
  observe() {
    this.callback([{ isIntersecting: true }])
  }

  disconnect() {}
}

describe('the scenes, playing', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.stubGlobal('IntersectionObserver', OnScreenObserver)
    vi.stubGlobal('matchMedia', () => ({ matches: false, addEventListener() {}, removeEventListener() {} }))
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('walks the draft from an empty strip to the pointer clicking Ahri\'s Hover', async () => {
    const wrapper = await mountSuspended(DesktopLandingDraftDemo, { props: { patch: PATCH } })
    await nextTick()

    expect(wrapper.findAll('.landing-rise.is-on')).toHaveLength(0)
    expect(wrapper.text()).not.toContain('Lock in Ahri')

    // The picks (400 + 3 × 260 ms), then the pointer reaches "Hover".
    vi.advanceTimersByTime(1180)
    await nextTick()
    expect(wrapper.findAll('.landing-rise.is-on')).toHaveLength(4)
    expect(wrapper.find('.landing-pointer').classes()).not.toContain('opacity-0')

    // The click hovers Ahri; the pointer steps aside.
    vi.advanceTimersByTime(800)
    await nextTick()
    expect(wrapper.text()).toContain('Lock in Ahri')
    vi.advanceTimersByTime(500)
    await nextTick()
    expect(wrapper.find('.landing-pointer').classes()).toContain('opacity-0')
  })

  it('takes the recap\'s playhead to the triple kill, cuts a range the app names, then saves it', async () => {
    const wrapper = await mountSuspended(DesktopLandingRecapDemo, { props: { patch: null } })
    await nextTick()

    expect(wrapper.text()).toContain('1:16 / 31:40')
    expect(wrapper.text()).toContain('Saved clips · 2')

    // The playhead travels (500 + 1400 ms), the range is dragged (900 ms), and the draft row shows its name.
    vi.advanceTimersByTime(500 + 1400 + 900)
    await nextTick()
    expect(wrapper.text()).toContain('13:23 / 31:40')
    expect(wrapper.text()).toContain('Triple kill on Yone')
    expect(wrapper.text()).toContain('12:48 → 13:33')

    vi.advanceTimersByTime(1400)
    await nextTick()
    expect(wrapper.text()).toContain('Saved clips · 3')
    expect(wrapper.text()).not.toContain('12:48 → 13:33')
  })
})
