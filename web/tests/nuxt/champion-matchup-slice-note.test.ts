import { mountSuspended } from '@nuxt/test-utils/runtime'
import { describe, expect, it } from 'vitest'
import { defineComponent, h } from 'vue'
import { ChampionMatchupSliceNote, UApp } from '#components'

// `UApp` supplies the tooltip provider the warning dot's tooltip injects, as `app.vue` does.
function mountNote(games: number) {
  return mountSuspended(defineComponent({
    setup: () => () => h(UApp, null, () => h(ChampionMatchupSliceNote, { games, opponentName: 'Zed' })),
  }))
}

/**
 * The matchup slice's sample line (#2355): the count is always visible, and a
 * slice under the 20-game floor carries the warning dot, its explanation in the
 * tooltip rather than written on the page.
 */
describe('champion matchup slice note', () => {
  const dot = '[role="img"]'

  it('states the count of a well-sampled matchup, with no warning', async () => {
    const wrapper = await mountNote(64)

    expect(wrapper.text()).toContain('Build against Zed')
    expect(wrapper.text()).toContain('64 games')
    expect(wrapper.find(dot).exists()).toBe(false)
  })

  it('marks a thin matchup with the dot, and writes no warning', async () => {
    const wrapper = await mountNote(1)

    expect(wrapper.get(dot).attributes('aria-label')).toBe('Only 1 game against Zed')
    expect(wrapper.text()).toContain('1 game')
    expect(wrapper.text()).not.toContain('Only')
    expect(wrapper.find('button').exists()).toBe(false)
  })

  it('draws the line at 20 games', async () => {
    const at = await mountNote(20)
    const under = await mountNote(19)

    expect(at.find(dot).exists()).toBe(false)
    expect(under.get(dot).attributes('aria-label')).toBe('Only 19 games against Zed')
  })
})
