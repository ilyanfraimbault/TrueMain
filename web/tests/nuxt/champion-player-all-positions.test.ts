import type { Component } from 'vue'
import { flushPromises } from '@vue/test-utils'
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime'
import { getQuery } from 'h3'
import { describe, expect, it, vi } from 'vitest'
import { ChampionFilters, ChampionMatchupSpread, ChampionPlayerHeading, UApp } from '#components'
import type { ChampionMatchupEntry, ChampionMatchups as ChampionMatchupsResponse } from '~~/shared/types/champions'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { ALL_POSITIONS, type PositionFilter } from '#common/utils/positions'

/**
 * The player champion page's "All positions" slice (#2158): the matchup spread
 * asks the player route for `ALL` and drops the `/matchup` links it cannot fill,
 * the heading names the slice, and the build filter offers the button only when
 * asked to.
 */

const champions: ChampionStaticListItem[] = [
  { championId: 39, name: 'Irelia', iconUrl: '/irelia.png' },
  { championId: 114, name: 'Fiora', iconUrl: '/fiora.png' },
  { championId: 80, name: 'Pantheon', iconUrl: '/pantheon.png' },
]

function entry(opponentChampionId: number, games: number, wins: number, laneWins: number, lanes: number): ChampionMatchupEntry {
  return {
    opponentChampionId,
    games,
    wins,
    winRate: wins / games,
    playRate: 0.1,
    winRateLowerBound: 0.2,
    winRateUpperBound: 0.8,
    laneWinRate: laneWins / lanes,
    decidedLaneGames: lanes,
    averageGoldDiffAt15: null,
    goldDiffLaneGames: 0,
    averageXpDiffAt15: null,
    xpDiffLaneGames: 0,
  }
}

const positions: string[] = []

registerEndpoint('/api/truemains/Aileri-KR1/champions/39/matchups', (event): ChampionMatchupsResponse => {
  const position = String(getQuery(event).position)
  positions.push(position)
  return {
    championId: 39,
    position,
    patch: null,
    matchups: position === 'JUNGLE' ? [] : [entry(114, 3, 2, 2, 3), entry(80, 1, 1, 1, 1)],
  }
})

async function mountMatchups(position: PositionFilter) {
  const wrapper = await mountSuspended(defineComponent({
    setup: () => () => h(UApp, null, () => h(ChampionMatchupSpread, {
      championId: 39,
      position,
      champions,
      nameTag: 'Aileri-KR1',
    })),
  }))
  await vi.waitFor(async () => {
    await flushPromises()
    expect(wrapper.find('[aria-label*="lanes won"]').exists() || wrapper.text().includes('No decided lane') || wrapper.text().includes('Not enough decided lanes')).toBe(true)
  })
  return wrapper
}

async function mountInApp(component: Component, props: Record<string, unknown>) {
  return mountSuspended(defineComponent({
    setup: () => () => h(UApp, null, () => h(component, props)),
  }))
}

describe('player champion page — all positions', () => {
  it('reads every position and links nowhere the matchup tool cannot answer', async () => {
    const wrapper = await mountMatchups(ALL_POSITIONS)

    expect(positions).toContain('ALL')
    const labels = wrapper.findAll('[aria-label*="lanes won"]').map(mark => mark.attributes('aria-label'))
    expect(labels).toContain('Fiora — 67% lanes won (3 decided lanes)')
    // A one-lane opponent is placed: the player slice has no lane floor.
    expect(labels).toContain('Pantheon — 100% lanes won (1 decided lane)')
    expect(wrapper.findAll('a[href^="/matchup"]')).toHaveLength(0)
  })

  it('keeps the matchup tool links on a single position', async () => {
    const wrapper = await mountMatchups('TOP')

    expect(wrapper.findAll('a[href^="/matchup"]').map(a => a.attributes('href')))
      .toContain('/matchup?champion=39&position=TOP&opponent=114')
  })

  it('says an empty slice has no decided lane on that role', async () => {
    const wrapper = await mountMatchups('JUNGLE')

    expect(wrapper.text()).toContain('Not enough decided lanes against any jungle opponent yet.')
  })

  it('names the slice in the build heading', async () => {
    const wrapper = await mountInApp(ChampionPlayerHeading, {
      championName: 'Irelia',
      championIconUrl: null,
      championId: 39,
      position: ALL_POSITIONS,
      totalGames: 21,
      totalWins: 11,
    })

    expect(wrapper.find('img[alt="All positions"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('21 games')

    const top = await mountInApp(ChampionPlayerHeading, {
      championName: 'Irelia',
      championIconUrl: null,
      championId: 39,
      position: 'TOP',
      totalGames: 16,
    })
    expect(top.find('img[alt="Top"]').exists()).toBe(true)
    expect(top.find('img[alt="All positions"]').exists()).toBe(false)
  })

  it('names All positions among the filters an empty slice clears', () => {
    const filters = ref<{ patch?: string, position?: string }>({ patch: '16.20', position: ALL_POSITIONS })
    const setFilter = vi.fn()
    const { activeFilterLabels, clearFilters } = usePlayerChampionEmptySlice({
      championId: computed(() => 39),
      filters: filters as never,
      setFilter,
      selectedPatch: computed(() => ''),
    })

    expect(activeFilterLabels.value).toEqual(['16.20', 'All positions'])
    filters.value = { position: 'UTILITY' }
    expect(activeFilterLabels.value).toEqual(['Support'])

    clearFilters()
    expect(setFilter).toHaveBeenCalledWith({ patch: null, position: null })
  })

  it('offers the All positions button only when asked to', async () => {
    const props = { selectedPatch: '16.20', selectedPosition: null, patchOptions: [] }
    const hidden = await mountInApp(ChampionFilters, props)
    expect(hidden.find('button[aria-label="All positions"]').exists()).toBe(false)

    const picked: unknown[] = []
    const shown = await mountInApp(ChampionFilters, {
      ...props,
      allPositions: true,
      'onUpdate:position': (value: unknown) => picked.push(value),
    })
    await shown.find('button[aria-label="All positions"]').trigger('click')
    expect(picked).toEqual([null])
  })
})
