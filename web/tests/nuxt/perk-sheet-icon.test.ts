import { mountSuspended } from '@nuxt/test-utils/runtime'
import { flushPromises } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { GameTooltipPerkIcon } from '#components'
import type { RuneSheet, StaticPerkData } from '#shared/types/static-data'

// A rune drawn from its tree's sprite sheet (#999). What it must keep from the
// single <img> it replaces: the rune's accessible name, its skeleton until the
// picture is in, and a working picture if the sheet does not load.
const perk: StaticPerkData = {
  id: 8005,
  name: 'Press the Attack',
  iconUrl: 'https://raw.communitydragon.org/16.19/plugins/rcp-be-lol-game-data/global/default/v1/perk-images/styles/precision/presstheattack/presstheattack.png',
}

function sheetAt(url: string): RuneSheet {
  return { url, cell: 64, gap: 2, columns: 4, rows: 4, perkIds: [8008, 8005] }
}

/** An `Image` whose load settles as told, since happy-dom fetches nothing. */
function stubImage(outcome: 'load' | 'error') {
  vi.stubGlobal('Image', class {
    onload: (() => void) | null = null
    onerror: (() => void) | null = null
    set src(_value: string) {
      queueMicrotask(() => (outcome === 'load' ? this.onload?.() : this.onerror?.()))
    }
  })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('GameTooltipPerkIcon with a rune sheet', () => {
  it('cuts the rune out of the sheet once it has loaded, named for assistive tech', async () => {
    stubImage('load')
    const wrapper = await mountSuspended(GameTooltipPerkIcon, {
      props: { perk, width: 32, height: 32, sheets: [sheetAt('/_rune-sheet/16.19/loads.webp')] },
    })
    await flushPromises()

    expect(wrapper.find('img').exists()).toBe(false)
    const icon = wrapper.find('[role="img"]')
    expect(icon.attributes('aria-label')).toBe('Press the Attack')
    const style = icon.attributes('style') ?? ''
    expect(style).toContain('/_rune-sheet/16.19/loads.webp')
    // Cell 1 of a 4-wide sheet, drawn at half its 64 px.
    expect(style).toContain('background-position: -33px 0px')
    expect(wrapper.find('.animate-pulse').exists()).toBe(false)
  })

  it('shows the skeleton while the sheet is still loading', async () => {
    vi.stubGlobal('Image', class { set src(_value: string) {} })
    const wrapper = await mountSuspended(GameTooltipPerkIcon, {
      props: { perk, width: 32, height: 32, sheets: [sheetAt('/_rune-sheet/16.19/pending.webp')] },
    })
    await flushPromises()

    expect(wrapper.find('.animate-pulse').exists()).toBe(true)
    expect(wrapper.find('[role="img"]').attributes('style') ?? '').not.toContain('background-image')
  })

  it('falls back to the rune\'s own icon when the sheet fails', async () => {
    stubImage('error')
    const wrapper = await mountSuspended(GameTooltipPerkIcon, {
      props: { perk, width: 32, height: 32, sheets: [sheetAt('/_rune-sheet/16.19/fails.webp')] },
    })
    await flushPromises()

    const img = wrapper.find('img')
    expect(img.attributes('src')).toContain('/_ipx/')
    expect(img.attributes('alt')).toBe('Press the Attack')
    expect(wrapper.find('[role="img"]').exists()).toBe(false)
  })

  it('draws the usual icon when no sheet holds the rune', async () => {
    const wrapper = await mountSuspended(GameTooltipPerkIcon, {
      props: { perk, width: 32, height: 32, sheets: [{ ...sheetAt('/_rune-sheet/16.19/other.webp'), perkIds: [1] }] },
    })

    expect(wrapper.find('img').attributes('alt')).toBe('Press the Attack')
  })
})
