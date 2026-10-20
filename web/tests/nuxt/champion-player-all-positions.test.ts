import type { Component } from 'vue'
import { flushPromises } from '@vue/test-utils'
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime'
import { getQuery } from 'h3'
import { describe, expect, it, vi } from 'vitest'
import { ChampionFilters, ChampionMatchups, ChampionPlayerHeading, UApp } from '#components'
import type { ChampionMatchupEntry, ChampionMatchups as ChampionMatchupsResponse } from '~~/shared/types/champions'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { ALL_POSITIONS, type PositionFilter } from '#common/utils/positions'

/**
 * The player champion page's "All positions" slice (#2158): the matchups card
 * asks the player route for `ALL` and drops the `/matchup` links it cannot fill,
 * the heading names the slice, and the build filter offers the button only when
 * asked to.
 */

const champions: ChampionStaticListItem[] = [
  { championId: 39, name: 'Irelia', iconUrl: '/irelia.png' },
  { championId: 114, name: 'Fiora', iconUrl: '/fiora.png' },
  { championId: 80, name: 'Pantheon', iconUrl: '/pantheon.png' },
]

function entry(opponentChampionId: number, games: number, wins: number): ChampionMatchupEntry {
  return {
    opponentChampionId,
    games,
    wins,
    winRate: wins / games,
    playRate: 0.1,
    winRateLowerBound: 0.2,
    winRateUpperBound: 0.8,
    laneWinRate: null,
    decidedLaneGames: 0,
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
    matchups: position === 'JUNGLE' ? [] : [entry(114, 3, 2), entry(80, 1, 1)],
  }
})

async function mountMatchups(position: PositionFilter) {
  const wrapper = await mountSuspended(defineComponent({
    setup: () => () => h(UApp, null, () => h(ChampionMatchups, {
      championId: 39,
      position,
      champions,
      nameTag: 'Aileri-KR1',
    })),
  }))
  await vi.waitFor(async () => {
    await flushPromises()
    expect(wrapper.text()).toMatch(/Best matchups|No recorded games/)
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
    expect(wrapper.text()).toContain('Fiora')
    // A one-game opponent is listed: the player slice has no games floor.
    expect(wrapper.text()).toContain('Pantheon')
    expect(wrapper.findAll('a[href^="/matchup"]')).toHaveLength(0)
  })

  it('keeps the matchup tool links on a single position', async () => {
    const wrapper = await mountMatchups('TOP')

    expect(wrapper.findAll('a[href^="/matchup"]').map(a => a.attributes('href')))
      .toContain('/matchup?champion=39&position=TOP&opponent=114')
  })

  it('says an empty player list holds no games, not none above a floor', async () => {
    const wrapper = await mountMatchups('JUNGLE')

    expect(wrapper.text()).toContain('No recorded games in the jungle yet.')
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
