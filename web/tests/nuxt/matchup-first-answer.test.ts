import { flushPromises } from '@vue/test-utils'
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime'
import { getQuery } from 'h3'
import { describe, expect, it, vi } from 'vitest'
import { BuilderMatchupRecommendation, UApp } from '#components'
import type { ChampionStaticListItem } from '#shared/types/static-data'

/**
 * The matchup page's first answer (#2361): while the composition build refines
 * the draft, the cached `?vs=` matchup slice stands in for the skeleton.
 */
const ahri: ChampionStaticListItem = { championId: 103, name: 'Ahri', iconUrl: '/ahri.png' }
const zed: ChampionStaticListItem = { championId: 238, name: 'Zed', iconUrl: '/zed.png' }

const requests: Record<string, unknown>[] = []
registerEndpoint('/api/champions/103', (event) => {
  const query = getQuery(event)
  requests.push(query)
  return {
    championId: 103,
    patch: '16.21',
    position: 'MIDDLE',
    eloBracket: 'ALL',
    eloCoverage: 1,
    minSampleMet: true,
    totalGames: query.opponentChampionId ? 42 : 900,
    totalWins: 21,
    builds: [],
  }
})

function mountRecommendation(opponent: ChampionStaticListItem | null) {
  return mountSuspended(defineComponent({
    setup: () => () => h(UApp, null, () => h(BuilderMatchupRecommendation, {
      loading: true,
      recommendation: null,
      playedChampionId: 103,
      playedPosition: 'MIDDLE',
      playedChampion: ahri,
      opponentChampion: opponent,
      draftRequest: null,
      champions: [ahri, zed],
      gamesDrawerOpen: false,
    })),
  }))
}

describe('matchup first answer', () => {
  it('reads the matchup slice while the draft refines it', async () => {
    requests.length = 0
    const wrapper = await mountRecommendation(zed)
    await vi.waitFor(async () => {
      await flushPromises()
      expect(requests).toContainEqual({ position: 'MIDDLE', opponentChampionId: '238' })
    })

    expect(wrapper.text()).toContain('Ahri vs Zed')
    expect(wrapper.text()).toContain('Refining with the draft')
  })

  it('keeps the plain skeleton without a role opponent', async () => {
    requests.length = 0
    const wrapper = await mountRecommendation(null)
    await flushPromises()

    expect(requests).toEqual([])
    expect(wrapper.text()).not.toContain('Refining with the draft')
  })
})
