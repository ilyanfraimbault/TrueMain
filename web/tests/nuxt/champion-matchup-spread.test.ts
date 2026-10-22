import { flushPromises } from '@vue/test-utils'
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime'
import { getQuery } from 'h3'
import { describe, expect, it, vi } from 'vitest'
import { ChampionMatchupSpread, UApp } from '#components'
import type { ChampionMatchupEntry, ChampionMatchups } from '~~/shared/types/champions'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import type { ChampionPosition } from '#common/utils/positions'

/**
 * The champion page's matchup spread (#2094), mounted against a mocked matchups
 * endpoint: what it places, what it leaves off, and that it follows the page's
 * lane with no role picker of its own.
 */

const champions: ChampionStaticListItem[] = [
  { championId: 1, name: 'Veigar', iconUrl: '/veigar.png' },
  { championId: 2, name: 'Xerath', iconUrl: '/xerath.png' },
  { championId: 3, name: 'Ryze', iconUrl: '/ryze.png' },
  { championId: 4, name: 'Lux', iconUrl: '/lux.png' },
  { championId: 5, name: 'Zoe', iconUrl: '' },
]

function entry(opponentChampionId: number, laneWinRate: number | null, averageGoldDiffAt15: number | null = null): ChampionMatchupEntry {
  return {
    opponentChampionId,
    games: 60,
    wins: 30,
    winRate: 0.5,
    playRate: 0.1,
    winRateLowerBound: 0.37,
    winRateUpperBound: 0.63,
    laneWinRate,
    decidedLaneGames: 40,
    averageGoldDiffAt15,
    goldDiffLaneGames: averageGoldDiffAt15 === null ? 0 : 40,
    averageXpDiffAt15: null,
    xpDiffLaneGames: 0,
  }
}

function answer(championId: number, position: string, matchups: ChampionMatchupEntry[]): ChampionMatchups {
  return { championId, position, patch: '16.20', matchups }
}

const requests: string[] = []

// Veigar: one hard, one even, one easy lane, and one opponent with too few decided lanes.
registerEndpoint('/api/champions/1/matchups', (event) => {
  const { position } = getQuery(event) as { position: string }
  requests.push(position)
  return position === 'MIDDLE'
    ? answer(1, position, [entry(2, 0.425, -310), entry(3, 0.57, 220), entry(4, 0.51), entry(5, null)])
    : answer(1, position, [entry(4, 0.6)])
})

// A champion with no decided lane anywhere.
registerEndpoint('/api/champions/9/matchups', event =>
  answer(9, String(getQuery(event).position), [entry(2, null), entry(3, null)]))

async function mountSpread(championId: number, initial: ChampionPosition) {
  const position = ref(initial)
  const wrapper = await mountSuspended(defineComponent({
    setup: () => () => h(UApp, null, () => h(ChampionMatchupSpread, { championId, position: position.value, champions, patch: '16.20' })),
  }))
  await vi.waitFor(async () => {
    await flushPromises()
    expect(wrapper.find('a[href^="/matchup"]').exists() || wrapper.text().includes('Not enough games')).toBe(true)
  })
  return Object.assign(wrapper, { position })
}

describe('champion matchup spread', () => {
  it('places measured opponents and nothing else', async () => {
    const wrapper = await mountSpread(1, 'MIDDLE')

    const links = wrapper.findAll('a[href^="/matchup"]')
    expect(links.map(a => a.attributes('href'))).toEqual([
      '/matchup?champion=1&position=MIDDLE&opponent=2',
      '/matchup?champion=1&position=MIDDLE&opponent=4',
      '/matchup?champion=1&position=MIDDLE&opponent=3',
    ])
    expect(links[0]!.attributes('aria-label')).toBe('Xerath: 43% WR')

    // Only the axis: no caption, no band labels, no counts or extremes under it.
    const text = wrapper.text()
    expect(text).toContain('Matchups')
    expect(text).not.toMatch(/a lane is won when|Favourable|Hardest|Easiest|lanes won|too few decided lanes/)
  })

  it('has no role picker and asks the API again when the page changes lane', async () => {
    const wrapper = await mountSpread(1, 'MIDDLE')
    requests.length = 0

    expect(wrapper.find('button[aria-label="Top"]').exists()).toBe(false)
    wrapper.position.value = 'TOP'
    await vi.waitFor(async () => {
      await flushPromises()
      expect(wrapper.findAll('a[href^="/matchup"]').map(a => a.attributes('href')))
        .toEqual(['/matchup?champion=1&position=TOP&opponent=4'])
    })
    expect(requests).toEqual(['TOP'])
  })

  it('says so when no opponent has enough games', async () => {
    const wrapper = await mountSpread(9, 'UTILITY')

    expect(wrapper.find('a[href^="/matchup"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Not enough games against any support opponent yet.')
  })
})
