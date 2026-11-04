import { mountSuspended } from '@nuxt/test-utils/runtime'
import { flushPromises } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import { GameTooltipItemIcon, UApp } from '#components'
import type { StaticItemData } from '#shared/types/static-data'
import { resetPaintedIcons } from '#common/utils/icon-repaint'

// The first hover arms the icon's tooltip, which re-creates the icon's <img> under
// the pointer. A picture already on screen must come back drawn — not as a loading
// skeleton fading in again, which blinked the icon out and back.

const item = {
  id: 3078,
  name: 'Trinity Force',
  iconUrl: 'https://ddragon.leagueoflegends.com/cdn/16.20.1/img/item/3078.png',
  description: '',
  totalGold: 3333,
} as unknown as StaticItemData

// `UApp` supplies the tooltip provider the icon tooltips inject, as `app.vue` does.
const Icon = defineComponent({
  setup: () => () => h(UApp, null, () => h(GameTooltipItemIcon, { item })),
})

async function hoverOnce(wrapper: Awaited<ReturnType<typeof mountSuspended>>) {
  await wrapper.find('span.contents').trigger('mouseover')
  return wrapper.find('img')
}

describe('LazyTooltip icon re-creation', () => {
  beforeEach(resetPaintedIcons)

  it('redraws a loaded icon at once when the first hover re-creates it', async () => {
    const wrapper = await mountSuspended(Icon)
    const before = wrapper.find('img')
    await before.trigger('load')

    const after = await hoverOnce(wrapper)

    expect(after.element).not.toBe(before.element)
    expect(after.attributes('decoding')).toBe('sync')
    expect(after.classes()).toContain('opacity-100')
    expect(after.classes()).not.toContain('transition-opacity')
    expect(wrapper.find('span.absolute').exists()).toBe(false)
  })

  it('keeps the skeleton for an icon hovered before its picture arrived', async () => {
    const wrapper = await mountSuspended(Icon)

    const after = await hoverOnce(wrapper)

    expect(after.attributes('decoding')).toBeUndefined()
    expect(after.classes()).toContain('opacity-0')
    expect(wrapper.find('span.absolute').exists()).toBe(true)
  })

  describe('with a picture on screen', () => {
    const CURRENT_SRC = 'https://truemain.test/_ipx/f_webp&s_64x64/item/3078.png'
    let warmed: { src: string }[]
    let settleDecode: { resolve: () => void, reject: () => void }

    beforeEach(() => {
      warmed = []
      vi.stubGlobal('Image', class {
        src = ''
        constructor() {
          warmed.push(this)
        }

        decode() {
          return new Promise<void>((resolve, reject) => {
            settleDecode = { resolve, reject: () => reject(new Error('decode')) }
          })
        }
      })
    })
    afterEach(() => vi.unstubAllGlobals())

    // The test DOM loads no picture, so the icon's is declared drawn by hand.
    async function mountDrawn() {
      const wrapper = await mountSuspended(Icon)
      const img = wrapper.find('img')
      await img.trigger('load')
      Object.defineProperties(img.element, {
        complete: { value: true },
        naturalWidth: { value: 64 },
        currentSrc: { value: CURRENT_SRC },
      })
      return wrapper
    }

    // The bug itself: an opaque <img> re-created before its picture is back in memory
    // paints its alt text. The icon must stay in place until the picture is warm.
    it('keeps the icon in place until its picture is warm', async () => {
      const wrapper = await mountDrawn()
      const before = wrapper.find('img').element

      await wrapper.find('span.contents').trigger('mouseover')
      await wrapper.find('span.contents').trigger('mouseover')

      expect(warmed).toEqual([{ src: CURRENT_SRC }])
      expect(wrapper.find('img').element).toBe(before)

      settleDecode.resolve()
      await flushPromises()

      expect(wrapper.find('img').element).not.toBe(before)
      expect(wrapper.find('img').attributes('decoding')).toBe('sync')
    })

    it('still arms the tooltip when the picture fails to decode', async () => {
      const wrapper = await mountDrawn()
      const before = wrapper.find('img').element

      await wrapper.find('span.contents').trigger('mouseover')
      settleDecode.reject()
      await flushPromises()

      expect(wrapper.find('span.contents').exists()).toBe(false)
      expect(wrapper.find('img').element).not.toBe(before)
    })
  })

  it('never lets an icon without a picture paint its alt text', async () => {
    const wrapper = await mountSuspended(Icon)

    expect(wrapper.find('img').classes()).toContain('text-transparent')
  })
})
