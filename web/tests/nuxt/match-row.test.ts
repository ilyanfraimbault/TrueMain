import { mountSuspended } from '@nuxt/test-utils/runtime'
import { describe, expect, it } from 'vitest'
import { defineComponent, h, type Component } from 'vue'
import { MatchHistoryList, MatchRow, MatchRowTeams, UApp } from '#components'
import type { MatchSummaryParticipant, MatchSummaryResponse } from '#shared/types/matches'
import type { ChampionStaticListItem, RuneTreeResponse } from '#shared/types/static-data'

// The match row on a diet (#2129): its ten composition icons are plain <img>s
// on the canonical URL with the skeleton as their own background, the champion
// lookup is built once by the list, and a row off screen is skipped by the
// browser (`content-visibility: auto`) without losing its accordion.

const icon = (name: string) => `https://ddragon.leagueoflegends.com/cdn/16.20.1/img/champion/${name}.png`
const champions: ChampionStaticListItem[] = [
  { championId: 86, name: 'Garen', iconUrl: icon('Garen') },
  { championId: 64, name: 'Lee Sin', iconUrl: icon('LeeSin') },
  { championId: 103, name: 'Ahri', iconUrl: icon('Ahri') },
]
const championById = new Map(champions.map(c => [c.championId, c]))

function participant(championId: number, teamId: number, position: string | null): MatchSummaryParticipant {
  return { championId, teamId, position, gameName: null, tagLine: null }
}

const participants = [
  participant(64, 200, 'JUNGLE'),
  participant(86, 100, 'TOP'),
  participant(103, 200, 'MIDDLE'),
  // Unknown to the static list.
  participant(9999, 100, 'JUNGLE'),
]

function match(matchId: string): MatchSummaryResponse {
  return {
    matchId,
    queueId: 420,
    gameMode: 'CLASSIC',
    gameStartTimeUtc: '2026-10-18T20:00:00Z',
    gameDurationSeconds: 1800,
    self: {
      championId: 86,
      championLevel: 16,
      summoner1Id: 4,
      summoner2Id: 12,
      primaryStyleId: 8000,
      subStyleId: 8400,
      keystoneId: 8010,
      kills: 7,
      deaths: 2,
      assists: 5,
      cs: 210,
      killParticipation: 0.5,
      items: [0, 0, 0, 0, 0, 0],
      trinketItemId: 0,
      roleBoundItemId: 0,
      teamId: 100,
      position: 'TOP',
      win: true,
      lpDelta: 18,
      performanceScore: 61,
      placement: 2,
    } as MatchSummaryResponse['self'],
    participants,
  }
}

const runeTree: RuneTreeResponse = { styles: [{ id: 8000 }] as unknown as RuneTreeResponse['styles'], perks: {}, perkStyles: {}, shardSlots: [] }
// The loadout's icon tooltips need the provider `UApp` sets up, as `app.vue` does.
function mountInApp(component: Component, props: Record<string, unknown>) {
  return mountSuspended(defineComponent({ setup: () => () => h(UApp, null, () => h(component, props)) }))
}

const items = { 1001: { id: 1001 } } as unknown as Record<number, never>
const summonerSpells = { 4: { id: 4 } } as unknown as Record<number, never>

describe('MatchRowTeams', () => {
  it('draws the player\'s team over the enemy\'s, in lane order, as plain canonical <img>s', async () => {
    const wrapper = await mountSuspended(MatchRowTeams, { props: { participants, selfTeamId: 100, championById } })

    const imgs = wrapper.findAll('img')
    expect(imgs.map(img => img.attributes('alt'))).toEqual(['Garen · Top', 'Lee Sin · Jungle', 'Ahri · Middle'])
    for (const img of imgs) {
      expect(img.attributes('src')).toContain('/_ipx/')
      expect(img.attributes('src')).toContain('f_webp')
      expect(img.attributes('srcset')).toBeUndefined()
      expect(img.classes()).toContain('img-skeleton')
    }
  })

  it('takes the skeleton off once the picture has loaded', async () => {
    const wrapper = await mountSuspended(MatchRowTeams, { props: { participants, selfTeamId: 100, championById } })

    const img = wrapper.find('img')
    await img.trigger('load')
    expect(img.classes()).not.toContain('img-skeleton')
  })

  it('draws the hollow box for a champion the static list does not know', async () => {
    const wrapper = await mountSuspended(MatchRowTeams, { props: { participants, selfTeamId: 100, championById } })

    const unknown = wrapper.find('[role="img"]')
    expect(unknown.exists()).toBe(true)
    expect(unknown.attributes('aria-label')).toContain('Jungle')
    expect(unknown.classes()).toContain('ring-1')
    expect(unknown.classes()).not.toContain('animate-pulse')
  })
})

describe('MatchRow', () => {
  const props = { match: match('EUW1_1'), champions, items, summonerSpells, runeTree }

  it('is skipped off screen, keeping the last height it rendered', async () => {
    const wrapper = await mountInApp(MatchRow, props)

    const row = wrapper.find('article')
    expect(row.classes()).toContain('[content-visibility:auto]')
    expect(row.classes()).toContain('[contain-intrinsic-block-size:auto_54px]')
    expect(row.attributes('aria-label')).toBe('Victory as Garen, 7/2/5')
  })

  it('reads the champions through the lookup its host built', async () => {
    const renamed = new Map(championById).set(86, { championId: 86, name: 'Hosted Garen', iconUrl: icon('Garen') })
    const wrapper = await mountInApp(MatchRow, { ...props, championById: renamed })

    expect(wrapper.find('article').attributes('aria-label')).toBe('Victory as Hosted Garen, 7/2/5')
  })

  it('stays an accordion: the row toggles from the keyboard', async () => {
    const wrapper = await mountInApp(MatchRow, { ...props, nameTag: 'Vexsmith-2104' })

    const toggle = wrapper.find('[role="button"]')
    expect(toggle.attributes('tabindex')).toBe('0')
    expect(toggle.attributes('aria-expanded')).toBe('false')
    await toggle.trigger('keydown', { key: 'Enter' })
    expect(toggle.attributes('aria-expanded')).toBe('true')
  })
})

describe('MatchHistoryList', () => {
  it('draws one row per game, the composition icons from its own champion lookup', async () => {
    const wrapper = await mountInApp(MatchHistoryList, {
      matches: [match('EUW1_1'), match('EUW1_2')],
      loading: false,
      total: 2,
      pageSize: 20,
      page: 1,
      champions,
      items,
      summonerSpells,
      runeTree,
    })

    const rows = wrapper.findAll('article')
    expect(rows).toHaveLength(2)
    expect(rows[0]!.attributes('aria-label')).toBe('Victory as Garen, 7/2/5')
    expect(rows[0]!.findAll('img[alt="Ahri · Middle"]')).toHaveLength(1)
  })
})
