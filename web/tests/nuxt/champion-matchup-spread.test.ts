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
 * endpoint: what it places, what it leaves off, and that its own role picker
 * re-asks the API for the lane it picks.
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

async function mountSpread(championId: number, position: ChampionPosition) {
  const wrapper = await mountSuspended(defineComponent({
    setup: () => () => h(UApp, null, () => h(ChampionMatchupSpread, { championId, position, champions, patch: '16.20' })),
  }))
  await vi.waitFor(async () => {
    await flushPromises()
    expect(wrapper.text()).toMatch(/a lane is won when|Not enough decided lanes/)
  })
  return wrapper
}

describe('champion matchup spread', () => {
  it('places measured opponents, names both ends and counts the rest', async () => {
    const wrapper = await mountSpread(1, 'MIDDLE')

    const links = wrapper.findAll('a[href^="/matchup"]')
    expect(links.map(a => a.attributes('href'))).toEqual([
      '/matchup?champion=1&position=MIDDLE&opponent=2',
      '/matchup?champion=1&position=MIDDLE&opponent=4',
      '/matchup?champion=1&position=MIDDLE&opponent=3',
    ])
    expect(links[0]!.attributes('aria-label')).toBe('Xerath — 43% lanes won (−7.5 pts) · 40 decided lanes · −310 gold at 15')

    const text = wrapper.text()
    expect(text).toContain('3 opponents · a lane is won when ahead in gold at 15 min')
    expect(text).toMatch(/Favourable\s*1\s*Even\s*1\s*Hard\s*1/)
    expect(text).toMatch(/Hardest\s*Xerath\s*−7\.5 pts/)
    expect(text).toMatch(/Easiest\s*Ryze\s*\+7\.0 pts/)
    expect(text).toContain('Veigar lanes won')
    expect(text).toContain('1 more opponent has too few decided lanes to place.')
  })

  it('asks the API again for the lane its role picker selects', async () => {
    const wrapper = await mountSpread(1, 'MIDDLE')
    requests.length = 0

    await wrapper.find('button[aria-label="Top"]').trigger('click')
    await vi.waitFor(async () => {
      await flushPromises()
      expect(wrapper.findAll('a[href^="/matchup"]').map(a => a.attributes('href')))
        .toEqual(['/matchup?champion=1&position=TOP&opponent=4'])
    })
    expect(requests).toEqual(['TOP'])
  })

  it('says so when no opponent has enough decided lanes', async () => {
    const wrapper = await mountSpread(9, 'UTILITY')

    expect(wrapper.find('a[href^="/matchup"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('Not enough decided lanes against any support opponent yet.')
  })
})
