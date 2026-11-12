import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref, watch, type Ref } from 'vue'
import BuildMains from '../app/components/build/BuildMains.vue'

// The build view's truemains outside Nuxt: the champion's top mains read from the
// leaderboard, a click selecting one, and what it says when there are none or the read
// failed. Auto-imports and child components are stand-ins.
let states: Map<string, Ref<unknown>>
const apiGet = vi.fn()

const leaderRow = (gameName: string) => ({
  identity: { gameName, tagLine: 'EUW', platformId: 'EUW1', profileIconId: 1, summonerLevel: 30 },
  region: 'europe',
  ranked: null,
  stats: { wins: 10, losses: 5, winRate: 0.67 },
  topChampions: [{ championId: 7, primaryKeystoneId: 8010, firstItemId: 3078 }],
})

const global = {
  stubs: {
    AppSearch: true,
    Account: { props: ['identity', 'region'], template: '<span class="account" :data-region="region">{{ identity.gameName }}</span>' },
    USkeleton: true,
    UEmpty: { props: ['description'], template: '<p class="empty">{{ description }}</p>' },
    UIcon: true,
    GameTooltipPerkIcon: true,
    GameTooltipItemIcon: true,
    RankEmblem: true,
  },
}

beforeEach(() => {
  states = new Map()
  apiGet.mockReset()
  vi.stubGlobal('useState', (key: string, init: () => unknown) => {
    if (!states.has(key)) states.set(key, ref(init()))
    return states.get(key)
  })
  vi.stubGlobal('apiGet', apiGet)
  vi.stubGlobal('ref', ref)
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('watch', watch)
  vi.stubGlobal('useStaticData', () => ({ runeTree: ref(null), items: ref(null) }))
  vi.stubGlobal('useChampionStatics', () => ({ patch: ref('16.4') }))
  vi.stubGlobal('useBuildResolvers', () => ({ perk: () => null, item: () => null }))
  vi.stubGlobal('useFavoriteMains', () => computed(() => []))
  vi.stubGlobal('useTruemainBuild', () => ({ load: vi.fn(), answerOf: () => null }))
  vi.stubGlobal('favoriteNameTag', (gameName: string, tagLine: string) => `${gameName}-${tagLine}`)
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('BuildMains', () => {
  it('lists the champion\'s top mains from the leaderboard and selects one on click', async () => {
    apiGet.mockResolvedValue({ rows: [leaderRow('Faker'), leaderRow('Chovy')] })
    const wrapper = mount(BuildMains, { props: { championId: 7, position: 'MIDDLE' }, global })
    await flushPromises()

    expect(apiGet).toHaveBeenCalledWith('/truemains', { championId: 7, pageSize: 5 })
    expect(wrapper.findAll('.account').map(account => account.text())).toEqual(['Faker', 'Chovy'])
    expect(wrapper.find('.account').attributes('data-region')).toBe('europe')

    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('select')?.[0]?.[0]).toMatchObject({ identity: { gameName: 'Faker' } })
  })

  it('says so when the champion has no tracked main, and when the list could not be read', async () => {
    apiGet.mockResolvedValueOnce({ rows: [] })
    const empty = mount(BuildMains, { props: { championId: 8, position: 'TOP' }, global })
    await flushPromises()
    expect(empty.find('.empty').text()).toContain('No tracked truemains')

    apiGet.mockRejectedValueOnce(new Error('down'))
    const failed = mount(BuildMains, { props: { championId: 9, position: 'TOP' }, global })
    await flushPromises()
    expect(failed.text()).toContain('could not be loaded')
  })
})
