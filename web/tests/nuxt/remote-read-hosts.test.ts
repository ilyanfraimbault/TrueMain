import { flushPromises } from '@vue/test-utils'
import { mockNuxtImport, mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime'
import { createError } from 'h3'
import { describe, expect, it } from 'vitest'
import { defineComponent, h, ref } from 'vue'
import { FavoritesPlayerCard, MatchHistoryList } from '#components'

/**
 * The hosts of a remote read (#2476): a 404 is "not found", anything else is an
 * error each host says as one — never an empty history, never "Unranked", never
 * an unknown player.
 */

// The favorites card holds its reads until it nears the viewport.
mockNuxtImport('useVisibleOnce', () => () => ref(true))

const PROFILE = {
  identity: { gameName: 'Found', tagLine: 'RR', platformId: 'EUW1', profileIconId: 1, summonerLevel: 100 },
  ranked: null,
  mains: [],
  dedication: null,
  positions: [],
  persona: null,
}
let profileReads = 0
registerEndpoint('/api/truemains/Found-RR/profile', () => {
  profileReads++
  return PROFILE
})
for (const path of ['profile', 'matches', 'matches/EUW1_1']) {
  registerEndpoint(`/api/truemains/Outage-RR/${path}`, () => {
    throw createError({ statusCode: 502 })
  })
  registerEndpoint(`/api/truemains/Gone-RR/${path}`, () => {
    throw createError({ statusCode: 404 })
  })
}

async function inSetup<T>(read: () => T): Promise<T> {
  let result!: T
  await mountSuspended(defineComponent({
    setup() {
      result = read()
      return () => h('div')
    },
  }))
  await flushPromises()
  return result
}

describe('useMatchDetail', () => {
  it('reads a 404 as not found', async () => {
    const detail = await inSetup(() => useMatchDetail('Gone-RR', 'EUW1_1'))
    await flushPromises()

    expect(detail.notFound.value).toBe(true)
    expect(detail.error.value).toBeFalsy()
  })

  it('reads an outage as an error, not as a missing game', async () => {
    const detail = await inSetup(() => useMatchDetail('Outage-RR', 'EUW1_1'))
    await flushPromises()

    expect(detail.notFound.value).toBe(false)
    expect(detail.error.value).toBeTruthy()
  })
})

describe('client truemain reads', () => {
  it('reads an outage on the matches as an error state', async () => {
    const matches = await inSetup(() => useTruemainMatches('Outage-RR', 1))

    expect(matches.state.value.kind).toBe('error')
    expect(matches.notFound.value).toBe(false)
  })

  it('reloads the profile from a cold state on refresh', async () => {
    const profile = await inSetup(() => useTruemainProfile('Found-RR'))
    const before = profileReads

    const pending = profile.refresh()
    expect(profile.data.value).toBeNull()
    expect(profile.isInitialLoading.value).toBe(true)
    await pending

    expect(profileReads).toBe(before + 1)
    expect(profile.data.value?.identity.gameName).toBe('Found')
  })
})

describe('MatchHistoryList', () => {
  const base = { matches: [], loading: false, total: 0, pageSize: 20, champions: [], items: {}, summonerSpells: {}, runeTree: null, page: 1 }

  it('says a failed page is a failure, not an empty history', async () => {
    const wrapper = await mountSuspended(MatchHistoryList, { props: { ...base, error: createError({ statusCode: 502 }) } })

    expect(wrapper.text()).toContain('The match history could not be loaded')
    expect(wrapper.text()).not.toContain('No games yet')
  })

  it('keeps the empty state for an answer with no games', async () => {
    const wrapper = await mountSuspended(MatchHistoryList, { props: base })

    expect(wrapper.text()).toContain('No games yet')
    expect(wrapper.text()).not.toContain('could not be loaded')
  })
})

describe('FavoritesPlayerCard', () => {
  const props = (nameTag: string) => ({
    favorite: { nameTag, gameName: nameTag.split('-')[0]!, tagLine: 'RR', region: null, profileIconId: null, addedAt: 0 },
    champions: [],
    items: {},
    summonerSpells: {},
    runeTree: null,
    patch: null,
  })

  it('says an outage is an outage, not "Unranked" or "no longer tracked"', async () => {
    const wrapper = await mountSuspended(FavoritesPlayerCard, { props: props('Outage-RR') })
    await flushPromises()

    expect(wrapper.text()).toContain('Rank unavailable')
    expect(wrapper.text()).toContain('could not be loaded')
    expect(wrapper.text()).not.toContain('Unranked')
    expect(wrapper.text()).not.toContain('no longer tracked')
  })

  it('still tells a player who is gone to unfollow them', async () => {
    const wrapper = await mountSuspended(FavoritesPlayerCard, { props: props('Gone-RR') })
    await flushPromises()

    expect(wrapper.text()).toContain('no longer tracked')
  })
})
