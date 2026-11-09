import { mountSuspended } from '@nuxt/test-utils/runtime'
import { describe, expect, it } from 'vitest'
import { DevDesignSystemColours } from '#components'
import { CHART_SERIES_PALETTE, defaultSeriesColor } from '~~/app/utils/chart-palette'
import { INK, ROSEGOLD, TIER_COLOR } from '~~/app/components/OgImage/theme'

// The places that cannot read a CSS variable take theme.css's values through
// the generated tokens: a token renamed there must not leave one of them blank.
describe('design token consumers', () => {
  it('caption every design-system swatch with a hex value', async () => {
    const wrapper = await mountSuspended(DevDesignSystemColours)

    expect(wrapper.text()).not.toContain('undefined')
    expect(wrapper.text()).toMatch(/#[0-9a-f]{6}/)
  })

  it('give the share card and the charts real colours', () => {
    for (const colour of [INK, ROSEGOLD, ...Object.values(TIER_COLOR), ...CHART_SERIES_PALETTE]) {
      expect(colour).toMatch(/^#[0-9a-f]{6}$/)
    }
    expect(defaultSeriesColor(CHART_SERIES_PALETTE.length)).toBe(CHART_SERIES_PALETTE[0])
  })
})
