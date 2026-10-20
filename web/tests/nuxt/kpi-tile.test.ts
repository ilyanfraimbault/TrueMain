import { mountSuspended } from '@nuxt/test-utils/runtime'
import { describe, expect, it } from 'vitest'
import { defineComponent, h } from 'vue'
import { KpiTile, UApp } from '#components'

// The form tile's second yardstick (#2076): the tier median sits beside the
// figure with the tier's emblem, and says what it was read from in its label.
const reference = { text: '2.90', tier: 'DIAMOND', hint: 'Median KDA of Diamond players on Mid: 2.90 (1,234 games)' }

// `UApp` supplies the tooltip provider the reference's tooltip injects, as `app.vue` does.
function mountTile(props: Record<string, unknown>) {
  return mountSuspended(defineComponent({
    setup: () => () => h(UApp, null, () => h(KpiTile, { label: 'KDA', value: '3.12', ...props })),
  }))
}

describe('KpiTile', () => {
  it('draws the tier reference with its emblem when one is given', async () => {
    const wrapper = await mountTile({ reference })

    const chip = wrapper.find('[data-testid="kpi-reference"]')
    expect(chip.exists()).toBe(true)
    expect(chip.text()).toContain('2.90')
    expect(chip.attributes('aria-label')).toBe(reference.hint)
    expect(chip.find('img').exists()).toBe(true)
  })

  it('draws no reference without one, nor while loading', async () => {
    const bare = await mountTile({})
    expect(bare.find('[data-testid="kpi-reference"]').exists()).toBe(false)

    const loading = await mountTile({ reference, loading: true })
    expect(loading.find('[data-testid="kpi-reference"]').exists()).toBe(false)
  })
})
