import { flushPromises } from '@vue/test-utils'
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { BuilderGamesDrawer, UApp } from '#components'

/**
 * The provenance drawer (#940) lists the games behind a recommendation. Games past match
 * retention count in it but keep nothing to list (#2368), so the drawer says how many.
 */
let truncatedGames: number | undefined = 12

registerEndpoint('/api/champions/103/composition-build/games', {
  method: 'POST',
  handler: () => ({
    championId: 103,
    position: 'MIDDLE',
    patch: null,
    page: 1,
    pageSize: 10,
    total: 0,
    truncatedGames,
    maxPossibleScore: 10,
    games: [],
  }),
})

afterEach(() => {
  document.body.innerHTML = ''
})

// Mounted closed, then opened: the drawer fetches on opening, not with the recommendation.
async function openDrawer() {
  const open = ref(false)
  await mountSuspended(defineComponent({
    setup: () => () => h(UApp, null, () => h(BuilderGamesDrawer, {
      open: open.value,
      championId: 103,
      draftRequest: { position: 'MIDDLE', allies: [], enemies: [{ championId: 238, position: 'MIDDLE' }] },
      championName: 'Ahri',
      champions: [],
      items: {},
      summonerSpells: {},
      runeTree: null,
    })),
  }), { attachTo: document.body })
  open.value = true
  await flushPromises()
}

describe('matchup games drawer', () => {
  it('says how many older games count without being listed', async () => {
    truncatedGames = 12
    await openDrawer()

    await vi.waitFor(() => expect(document.body.textContent).toContain('12 older games also count in this build.'))
  })

  it('says nothing when every game can be listed, or the API does not report them', async () => {
    truncatedGames = undefined
    await openDrawer()

    await vi.waitFor(() => expect(document.body.textContent).toContain('No sampled games to show.'))
    expect(document.body.textContent).not.toContain('also count')
  })
})
