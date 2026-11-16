import { flushPromises } from '@vue/test-utils'
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime'
import { getQuery } from 'h3'
import { describe, expect, it, vi } from 'vitest'
import { BuilderFallbackBuild, BuilderMatchupRecommendation, UApp } from '#components'
import type { CompositionBuildRequest, CompositionBuildResponse } from '#shared/contract/api'
import type { ChampionStaticListItem } from '#shared/types/static-data'

/**
 * The matchup page's first answer (#2361): while the composition build refines
 * the draft, the cached `?vs=` matchup slice stands in for the skeleton; once
 * refined, the recommendation says whether the draft weighed in.
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
    // Enough of a build for the header to count the slice's games.
    builds: [{ core: {}, buildTree: [], firstItemId: 6655 }],
  }
})

// The dev mock's answer for Ahri vs Zed, MIDDLE.
const recommendation = {
  championId: 103,
  position: 'MIDDLE',
  patch: '16.21',
  eloBracket: 'all',
  matchupRequested: true,
  matchupFound: true,
  confidence: { sampleSize: 3, candidatePoolSize: 3789, truemainGameCount: 2, maxPossibleScore: 16, meanSimilarity: 0.736 },
  lane: { measuredGames: 2, decidedGames: 1, winRate: 0.58, averageGoldDiffAt15: -86, averageXpDiffAt15: -92 },
  build: {
    gamesConsidered: 3,
    wins: 2,
    runePage: {
      primaryStyleId: 8100, primaryKeystoneId: 8112, primaryPerk1Id: 8126, primaryPerk2Id: 8136, primaryPerk3Id: 8106,
      secondaryStyleId: 8200, secondaryPerk1Id: 8234, secondaryPerk2Id: 8226,
      statOffense: 5007, statFlex: 5001, statDefense: 5001, games: 1, pickRate: 0.46, winRate: 0.53,
    },
    starterItems: { itemIds: [1056, 2003], games: 2, pickRate: 0.61, winRate: 0.536 },
    boots: { itemIds: [3020], games: 2, pickRate: 0.634, winRate: 0.549 },
    corePath: { itemIds: [6655, 4645, 3089], games: 1, pickRate: 0.4, winRate: 0.522 },
    summonerSpells: { spell1Id: 4, spell2Id: 14, games: 3, pickRate: 0.9, winRate: 0.522 },
    skillOrder: { sequence: ['Q', 'W', 'E'], games: 2, pickRate: 0.72, winRate: 0.526 },
    firstItemId: 6655,
    buildTree: [],
  },
} as unknown as CompositionBuildResponse

const matchupOnly: CompositionBuildRequest = {
  position: 'MIDDLE',
  allies: [{ championId: 103, position: 'MIDDLE' }],
  enemies: [{ championId: 238, position: 'MIDDLE' }],
}

function mountRecommendation(props: {
  loading: boolean
  opponent: ChampionStaticListItem | null
  recommendation?: CompositionBuildResponse | null
  draftRequest?: CompositionBuildRequest | null
}) {
  return mountSuspended(defineComponent({
    setup: () => () => h(UApp, null, () => h(BuilderMatchupRecommendation, {
      loading: props.loading,
      recommendation: props.recommendation ?? null,
      playedChampionId: 103,
      playedPosition: 'MIDDLE',
      playedChampion: ahri,
      opponentChampion: props.opponent,
      draftRequest: props.draftRequest ?? null,
      champions: [ahri, zed],
      gamesDrawerOpen: false,
    })),
  }))
}

describe('matchup first answer', () => {
  it('reads the matchup slice while the draft refines it', async () => {
    requests.length = 0
    const wrapper = await mountRecommendation({ loading: true, opponent: zed })
    await vi.waitFor(async () => {
      await flushPromises()
      expect(requests).toContainEqual({ position: 'MIDDLE', opponentChampionId: '238' })
      expect(wrapper.text()).toContain('42 games')
    })

    expect(wrapper.text()).toContain('Ahri vs Zed')
    expect(wrapper.text()).toContain('Refining with the draft')
  })

  it('keeps the plain skeleton without a role opponent', async () => {
    requests.length = 0
    const wrapper = await mountRecommendation({ loading: true, opponent: null })
    await flushPromises()

    expect(requests).toEqual([])
    expect(wrapper.text()).not.toContain('Refining with the draft')
  })

  it('titles the standard build as such, with no game count or refining badge', async () => {
    const wrapper = await mountSuspended(defineComponent({
      setup: () => () => h(UApp, null, () => h(BuilderFallbackBuild, {
        championId: 103,
        position: 'MIDDLE',
        championName: 'Ahri',
      })),
    }))
    await vi.waitFor(async () => {
      await flushPromises()
      expect(wrapper.text()).toContain('Ahri\'s standard build')
    })

    expect(wrapper.text()).not.toContain('900 games')
    expect(wrapper.text()).not.toContain('Refining with the draft')
  })

  it('labels the recommendation refined only when the draft holds more than the matchup', async () => {
    const matchup = await mountRecommendation({ loading: false, opponent: zed, recommendation, draftRequest: matchupOnly })
    await flushPromises()
    expect(matchup.text()).toContain('Recommended build for Ahri vs Zed')
    expect(matchup.text()).not.toContain('Refined with the draft')

    const drafted = await mountRecommendation({
      loading: false,
      opponent: zed,
      recommendation,
      draftRequest: { ...matchupOnly, allies: [...(matchupOnly.allies ?? []), { championId: 64, position: 'JUNGLE' }] },
    })
    await flushPromises()
    expect(drafted.text()).toContain('Refined with the draft')
  })
})
