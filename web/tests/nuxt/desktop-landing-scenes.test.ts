import { mountSuspended } from '@nuxt/test-utils/runtime'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import {
  DesktopLandingAfterGameDemo,
  DesktopLandingDraftDemo,
  DesktopLandingGameScreen,
  DesktopLandingInGame,
  DesktopLandingOverlayPanel,
  DesktopLandingRecapDemo,
} from '#components'
import type { RuneTreeResponse, StaticItemData } from '#shared/types/static-data'
import { sampleWinProbability } from '~/utils/desktop-landing-sample'

// `/download`'s drawn scenes (#2072). Each is server-rendered — and drawn for a
// visitor who asked for less motion — in its final state, so these pin that
// state: the values the app's own overlay sample produces, every panel on, the
// clip saved, the runes imported.

const item = (name: string, totalGold: number, tags: string[] = []): StaticItemData => ({ name, iconUrl: '', totalGold, tags } as unknown as StaticItemData)

/** The sample's items at round prices, so the lane sums are checkable by hand. */
const ITEMS: Record<number, StaticItemData> = {
  3157: item('Zhonya\'s Hourglass', 3250),
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

    expect(wrapper.text()).toContain('Zhonya\'s Hourglass')
    expect(wrapper.text()).toContain('650 gold to go')
    expect(wrapper.find('img').attributes('src')).toContain('3157.png')
    expect(wrapper.attributes('style')).toContain('width: 232px')
  })

  it('names the item before the item map is read, and draws an empty slot for its icon', async () => {
    const wrapper = await mountSuspended(DesktopLandingOverlayPanel, { props: { panel: 'next-item', patch: null, items: null } })

    expect(wrapper.text()).toContain('Zhonya\'s Hourglass')
    expect(wrapper.find('img').exists()).toBe(false)
  })

  it('splits the win probability the shared model gives the sample', async () => {
    const wrapper = await mountSuspended(DesktopLandingOverlayPanel, { props: { panel: 'win-probability', patch: PATCH, items: null } })
    const ours = Math.round(sampleWinProbability() * 100)

    expect(ours).toBeGreaterThan(50)
    expect(wrapper.text()).toContain(`${ours}%`)
    expect(wrapper.text()).toContain(`${100 - ours}%`)
  })

  it('draws the pace against the Diamond median', async () => {
    const wrapper = await mountSuspended(DesktopLandingOverlayPanel, { props: { panel: 'pace', patch: PATCH, items: null } })

    expect(wrapper.text()).toContain('7.8')
    expect(wrapper.text()).toContain('Diamond 7.3')
    expect(wrapper.text()).toContain('440')
    expect(wrapper.text()).toContain('Diamond 425')
    expect(wrapper.findAll('polyline')).toHaveLength(2)
  })

  it('sums item gold lane by lane, leaving out consumables and trinkets', async () => {
    const items = { ...ITEMS, 3047: item('Plated Steelcaps', 1200, ['Consumable']) }
    const wrapper = await mountSuspended(DesktopLandingOverlayPanel, { props: { panel: 'item-value', patch: PATCH, items } })
    const rows = wrapper.findAll('li')

    expect(rows).toHaveLength(5)
    // Bottom lane: Jinx holds a Cloak of Agility (600), Ezreal nothing priced here.
    expect(rows[3]!.text()).toContain('600')
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
  const runeTree = {
    styles: [
      { styleId: 8100, name: 'Domination', iconUrl: '', keystones: [8112], subRows: [[8126], [8136], [8135]] },
      { styleId: 8200, name: 'Sorcery', iconUrl: '', keystones: [8214], subRows: [[8226], [8210], [8232, 8237]] },
    ],
    perks: Object.fromEntries([8112, 8126, 8136, 8135, 8226, 8237].map(id => [id, { id, name: `Perk ${id}`, iconUrl: `https://ddragon.example/${id}.png` }])),
    perkStyles: { 8200: { id: 8200, name: 'Sorcery', iconUrl: 'https://ddragon.example/8200.png' } },
    shardSlots: [],
  } as unknown as RuneTreeResponse

  it('ends on Ahri hovered, her rune page drawn and the import toast', async () => {
    const wrapper = await mountSuspended(DesktopLandingDraftDemo, { props: { patch: PATCH, runeTree } })

    expect(wrapper.text()).toContain('Lock in Ahri')
    expect(wrapper.text()).toContain('Hovered')
    expect(wrapper.text()).toContain('Runes imported')
    expect(wrapper.findAll('.landing-rise.is-on')).toHaveLength(5)
    // Keystone, three primary runes, the secondary tree and its two runes.
    const runeIcons = wrapper.findAll('img').filter(img => img.attributes('src')?.includes('ddragon.example'))
    expect(runeIcons).toHaveLength(7)
  })

  it('draws empty rune slots until the rune tree is read', async () => {
    const wrapper = await mountSuspended(DesktopLandingDraftDemo, { props: { patch: null, runeTree: null } })

    expect(wrapper.findAll('span.rounded-full.bg-ink-950')).toHaveLength(7)
    expect(wrapper.findAll('img').filter(img => img.attributes('src')?.includes('champion/'))).toHaveLength(0)
  })
})

describe('LandingAfterGameDemo', () => {
  it('ends with every sample game landed in the goals and the curve drawn', async () => {
    const wrapper = await mountSuspended(DesktopLandingAfterGameDemo)

    expect(wrapper.text()).toContain('7.3 now · target 7.0 · 4/5 games')
    expect(wrapper.text()).toContain('3 of 5 met · 3/5 games')
    expect(wrapper.find('.landing-reveal').classes()).toContain('is-on')
    expect(wrapper.findAll('circle')).toHaveLength(5)
  })
})

describe('LandingRecapDemo', () => {
  it('ends on the triple kill saved and its clip at the head of the list', async () => {
    const wrapper = await mountSuspended(DesktopLandingRecapDemo)

    expect(wrapper.text()).toContain('Triple kill on Ahri')
    expect(wrapper.text()).toContain('Saved')
    expect(wrapper.findAll('.landing-clip.is-on')).toHaveLength(3)
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

  it('walks the draft from an empty strip to the pointer on the import icon, its tooltip, then the toast', async () => {
    const wrapper = await mountSuspended(DesktopLandingDraftDemo, { props: { patch: PATCH, runeTree: null } })
    await nextTick()

    expect(wrapper.findAll('.landing-rise.is-on')).toHaveLength(0)
    expect(wrapper.text()).not.toContain('Lock in Ahri')

    // The picks (700 + 3 × 260 ms), then the pointer reaches "Hover" (800 ms).
    vi.advanceTimersByTime(1480)
    await nextTick()
    expect(wrapper.findAll('.landing-rise.is-on')).toHaveLength(4)
    expect(wrapper.find('.landing-pointer').classes()).not.toContain('opacity-0')

    // The click hovers Ahri; the pointer travels to the import icon and its tooltip shows.
    vi.advanceTimersByTime(800 + 900 + 900)
    await nextTick()
    expect(wrapper.text()).toContain('Lock in Ahri')
    expect(wrapper.find('span.bg-inverted').classes()).toContain('opacity-100')

    // The click imports; the toast rises.
    vi.advanceTimersByTime(1100)
    await nextTick()
    expect(wrapper.find('span.bg-inverted').classes()).toContain('opacity-0')
    expect(wrapper.html()).toContain('translate-y-0 opacity-100')
  })

  it('moves the recap\'s playhead to the triple kill before the clip is cut', async () => {
    const wrapper = await mountSuspended(DesktopLandingRecapDemo)
    await nextTick()

    expect(wrapper.text()).toContain('1:16 / 31:40')
    expect(wrapper.findAll('.landing-clip.is-on')).toHaveLength(2)

    vi.advanceTimersByTime(500)
    await nextTick()
    expect(wrapper.text()).toContain('7:58 / 31:40')
    expect(wrapper.text()).toContain('Save clip')
  })

  it('fills the goals one game at a time', async () => {
    const wrapper = await mountSuspended(DesktopLandingAfterGameDemo)
    await nextTick()

    expect(wrapper.text()).toContain('Target 7.0 · 0/5 games')
    vi.advanceTimersByTime(600)
    await nextTick()
    expect(wrapper.text()).toContain('7.4 now · target 7.0 · 1/5 games')
  })
})
