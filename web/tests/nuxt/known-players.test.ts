import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, nextTick, ref } from 'vue'
import { PlayersPlayerAccountRow, PlayersPlayerLinks, SearchPlayerItem, UApp } from '#components'
import type { PersonaAccount } from '~~/shared/types/players'
import type { SearchPlayer } from '~~/shared/types/search'
import { useTruemainSearch } from '#common/composables/useTruemainSearch'

// Known players (#2016, #2018): the page's channel buttons and account rows,
// the search palette's player row, and the search composable carrying the
// players the same request answers.

const { apiFetch } = vi.hoisted(() => ({ apiFetch: vi.fn() }))
mockNuxtImport('useApiFetch', () => () => apiFetch)

afterEach(() => {
  apiFetch.mockReset()
  vi.useRealTimers()
})

// Tooltips need the provider `UApp` sets up for the whole site.
function inApp(component: object, props: Record<string, unknown>) {
  return mountSuspended(defineComponent({ setup: () => () => h(UApp, null, () => h(component, props)) }))
}

describe('PlayerLinks', () => {
  it('shows Twitch, YouTube and X as labelled icon buttons, and nothing else', async () => {
    const wrapper = await inApp(PlayersPlayerLinks, {
      links: [
        { kind: 'leaguepedia', url: 'https://lol.fandom.com/wiki/Faker' },
        { kind: 'twitch', url: 'https://www.twitch.tv/faker' },
        { kind: 'soop', url: 'https://www.sooplive.co.kr/station/faker' },
        { kind: 'youtube', url: 'https://www.youtube.com/@faker' },
        { kind: 'x', url: 'https://x.com/faker' },
      ],
    })

    const links = wrapper.findAll('a')
    expect(links.map(link => link.attributes('aria-label'))).toEqual(['Twitch', 'YouTube', 'X'])
    expect(links[0]!.attributes('href')).toBe('https://www.twitch.tv/faker')
    expect(links[0]!.attributes('rel')).toBe('noopener nofollow')
    expect(wrapper.text()).toBe('')
  })

  it('renders nothing when the person has none of those channels', async () => {
    const wrapper = await mountSuspended(PlayersPlayerLinks, {
      props: { links: [{ kind: 'website', url: 'https://example.com' }] },
    })

    expect(wrapper.find('ul').exists()).toBe(false)
  })
})

describe('PlayerAccountRow', () => {
  const account: PersonaAccount = {
    gameName: 'Aileri',
    tagLine: 'KR1',
    platformId: 'KR',
    region: 'korea',
    profileIconId: 6,
    lastPlayedAtUtc: null,
    ranked: { tier: 'CHALLENGER', division: 'I', leaguePoints: 1500, wins: 60, losses: 40 },
    topChampions: [],
  }

  it('ends on when the account last played', async () => {
    const yesterday = new Date(Date.now() - 86_400_000).toISOString()
    const wrapper = await mountSuspended(PlayersPlayerAccountRow, {
      props: { account: { ...account, lastPlayedAtUtc: yesterday }, championsById: new Map(), patch: '16.19.1' },
    })

    expect(wrapper.text()).toContain('Aileri')
    expect(wrapper.text()).toContain('60W 40L')
    expect(wrapper.text().trim().endsWith('yesterday')).toBe(true)
  })

  it('writes a dash when no game is stored, and Unranked without a rank', async () => {
    const wrapper = await mountSuspended(PlayersPlayerAccountRow, {
      props: { account: { ...account, ranked: null }, championsById: new Map(), patch: '16.19.1' },
    })

    expect(wrapper.text()).toContain('Unranked')
    expect(wrapper.text().trim().endsWith('—')).toBe(true)
  })
})

describe('SearchPlayerItem', () => {
  const player: SearchPlayer = {
    persona: { slug: 'irelking', displayName: 'Irelking' },
    accountCount: 3,
    profileIconId: 6,
    ranked: { tier: 'CHALLENGER', division: 'I', leaguePoints: 1500 },
  }

  it('draws the person with their account count and their best account face', async () => {
    const wrapper = await inApp(SearchPlayerItem, { player, patch: '16.19.1' })

    expect(wrapper.text()).toContain('Irelking')
    expect(wrapper.text()).toContain('3 accounts')
    expect(wrapper.find('img').attributes('src')).toContain('profileicon/6.png')
  })

  it('says one account in the singular', async () => {
    const wrapper = await mountSuspended(SearchPlayerItem, {
      props: { player: { ...player, accountCount: 1, ranked: null }, patch: '16.19.1' },
    })

    expect(wrapper.text()).toContain('1 account')
    expect(wrapper.text()).not.toContain('1 accounts')
  })
})

describe('useTruemainSearch', () => {
  it('exposes the known players the search answered, and clears them below the floor', async () => {
    vi.useFakeTimers()
    const player: SearchPlayer = { persona: { slug: 'irelking', displayName: 'Irelking' }, accountCount: 1, profileIconId: 6, ranked: null }
    apiFetch.mockResolvedValue({ results: [], players: [player] })

    const term = ref('')
    let search!: ReturnType<typeof useTruemainSearch>
    await mountSuspended(defineComponent({
      setup() {
        search = useTruemainSearch(term)
        return () => h('div')
      },
    }))

    term.value = 'irel'
    await nextTick()
    await vi.advanceTimersByTimeAsync(300)

    expect(apiFetch).toHaveBeenCalledWith('/truemains/search', expect.objectContaining({ query: { q: 'irel' } }))
    expect(search.players.value).toEqual([player])

    term.value = 'i'
    await nextTick()
    expect(search.players.value).toEqual([])
  })
})
