import { mountSuspended } from '@nuxt/test-utils/runtime'
import { beforeEach, describe, expect, it } from 'vitest'
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
})
