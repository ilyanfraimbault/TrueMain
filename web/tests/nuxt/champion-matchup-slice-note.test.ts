import { mountSuspended } from '@nuxt/test-utils/runtime'
import { describe, expect, it } from 'vitest'
import { ChampionMatchupSliceNote } from '#components'

/**
 * The matchup slice's sample line (#2355): the count is always visible, and a
 * slice under the 20-game floor is marked as thin without hovering anything.
 */
describe('champion matchup slice note', () => {
  it('states the count of a well-sampled matchup, with no warning', async () => {
    const wrapper = await mountSuspended(ChampionMatchupSliceNote, { props: { games: 64, opponentName: 'Zed' } })

    expect(wrapper.text()).toContain('Build against Zed · 64 games')
    expect(wrapper.text()).not.toContain('too few')
    expect(wrapper.find('button').exists()).toBe(false)
  })

  it('marks a thin matchup and offers the standard build', async () => {
    const wrapper = await mountSuspended(ChampionMatchupSliceNote, { props: { games: 1, opponentName: 'Zed' } })

    expect(wrapper.text()).toContain('Only 1 game against Zed — too few to trust this build.')
    await wrapper.get('button').trigger('click')
    expect(wrapper.emitted('clear')).toHaveLength(1)
  })

  it('draws the line at 20 games', async () => {
    const at = await mountSuspended(ChampionMatchupSliceNote, { props: { games: 20, opponentName: 'Zed' } })
    const under = await mountSuspended(ChampionMatchupSliceNote, { props: { games: 19, opponentName: 'Zed' } })

    expect(at.text()).not.toContain('too few')
    expect(under.text()).toContain('Only 19 games')
  })
})
