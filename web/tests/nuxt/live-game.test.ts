import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, nextTick, type Component } from 'vue'
import { flushPromises } from '@vue/test-utils'
import { LiveGame, LiveGameIdle, LiveGameParticipant, UApp } from '#components'
import type { LiveGameResponse, LiveParticipant } from '#shared/types/live'
import type { ChampionStaticListItem } from '#shared/types/static-data'

// The profile's Live tab (#2103): the game drawn as two teams with ranks, the
// empty states, and the one request per "Check now" — the tab never polls.

const { apiFetch } = vi.hoisted(() => ({ apiFetch: vi.fn() }))
mockNuxtImport('useApiFetch', () => () => apiFetch)

afterEach(() => {
  apiFetch.mockReset()
})

const icon = (name: string) => `https://ddragon.leagueoflegends.com/cdn/16.20.1/img/champion/${name}.png`
const champions: ChampionStaticListItem[] = [
  { championId: 103, name: 'Ahri', iconUrl: icon('Ahri') },
  { championId: 64, name: 'Lee Sin', iconUrl: icon('LeeSin') },
]

function player(overrides: Partial<LiveParticipant> = {}): LiveParticipant {
  return {
    gameName: 'Stranger',
    tagLine: 'EUW',
    championId: 64,
    profileIconId: 1,
    spell1Id: 4,
    spell2Id: 11,
    keystoneId: 8010,
    primaryStyleId: 8000,
    secondaryStyleId: 8400,
    position: 'JUNGLE',
    isBot: false,
    isSubject: false,
    isTracked: false,
    ranked: { tier: 'GOLD', division: 'II', leaguePoints: 42, wins: 30, losses: 20, winRate: 0.6 },
    ...overrides,
  }
}

const inGame: LiveGameResponse = {
  status: 'inGame',
  checkedAtUtc: new Date().toISOString(),
  game: {
    gameId: 1,
    platformId: 'EUW1',
    queueId: 420,
    mapId: 11,
    gameMode: 'CLASSIC',
    startedAtUtc: new Date(Date.now() - 600_000).toISOString(),
    gameLengthSeconds: 600,
    teams: [
      {
        teamId: 100,
        bans: [103],
        participants: [player({ gameName: 'Subject', championId: 103, position: 'MIDDLE', isSubject: true, isTracked: true })],
      },
      { teamId: 200, bans: [], participants: [player({ gameName: 'Tracked', tagLine: 'KR1', isTracked: true }), player({ ranked: null })] },
    ],
  },
}

// Tooltips need the provider `UApp` sets up for the whole site.
function inApp(component: Component, props: Record<string, unknown>) {
  return mountSuspended(defineComponent({ setup: () => () => h(UApp, null, () => h(component, props)) }))
}

async function mountLive(response: LiveGameResponse) {
  apiFetch.mockResolvedValue(response)
  const wrapper = await inApp(LiveGame, { nameTag: 'Subject-EUW1', champions, summonerSpells: null, runeTree: null })
  await flushPromises()
  await nextTick()
  return wrapper
}

describe('LiveGame', () => {
  it('draws both sides with the queue, the ranks and the profile player lifted', async () => {
    const wrapper = await mountLive(inGame)

    expect(apiFetch).toHaveBeenCalledTimes(1)
    expect(apiFetch.mock.calls[0]![0]).toBe('/truemains/Subject-EUW1/live')
    expect(wrapper.text()).toContain('Solo/Duo')
    expect(wrapper.find('[aria-label="Blue side"]').exists()).toBe(true)
    expect(wrapper.find('[aria-label="Red side"]').exists()).toBe(true)
    expect(wrapper.find('[data-subject]').text()).toContain('Subject')
    // The rank is a crest with its division; tier, LP and record live in its tooltip.
    expect(wrapper.text()).not.toContain('42 LP')
    expect(wrapper.findAll('img[alt="Gold rank"]').length).toBeGreaterThan(0)
    expect(wrapper.text()).toContain('Unranked')
  })

  it('shows the empty state for a player out of game, and asks again only on "Check now"', async () => {
    const wrapper = await mountLive({ status: 'notInGame', checkedAtUtc: new Date().toISOString(), game: null })

    expect(wrapper.text()).toContain('Not in a game')
    expect(wrapper.text()).toContain('Last checked')
    expect(apiFetch).toHaveBeenCalledTimes(1)

    await wrapper.find('button').trigger('click')
    await flushPromises()

    expect(apiFetch).toHaveBeenCalledTimes(2)
    expect(apiFetch.mock.calls[1]![1]).toMatchObject({ query: { refresh: true } })
  })

  it('tells Riot being unreachable apart from "not in a game"', async () => {
    const wrapper = await mountLive({ status: 'unavailable', checkedAtUtc: new Date().toISOString(), game: null })

    expect(wrapper.text()).toContain('Live game unavailable')
    expect(wrapper.text()).not.toContain('Not in a game')
  })
})

describe('LiveGameParticipant', () => {
  it('links a tracked player to their profile, never the profile player themself', async () => {
    const tracked = await inApp(LiveGameParticipant, {
      participant: player({ gameName: 'Tracked', tagLine: 'KR1', isTracked: true }),
      champion: champions[1],
      summonerSpells: null,
      runeTree: null,
    })
    expect(tracked.find('a[href="/truemains/Tracked-KR1"]').exists()).toBe(true)

    const subject = await inApp(LiveGameParticipant, {
      participant: player({ gameName: 'Subject', isTracked: true, isSubject: true }),
      champion: champions[1],
      summonerSpells: null,
      runeTree: null,
    })
    expect(subject.find('a[href^="/truemains/"]').exists()).toBe(false)
    expect(subject.text()).toContain('30W 20L · 60%')
  })
})

describe('LiveGameIdle', () => {
  it('emits check from its button', async () => {
    const wrapper = await inApp(LiveGameIdle, { status: 'notInGame', checkedAtUtc: null, now: new Date() })
    await wrapper.find('button').trigger('click')
    expect(wrapper.findComponent(LiveGameIdle).emitted('check')).toHaveLength(1)
  })
})
