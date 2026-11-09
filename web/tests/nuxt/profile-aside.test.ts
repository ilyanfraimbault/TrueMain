import { mockNuxtImport, mountSuspended } from '@nuxt/test-utils/runtime'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h } from 'vue'
import type { ProfileResponse } from '~~/shared/types/profile'
import { FavoriteToggle, ProfileAside, ProfileFollowToggle, UApp } from '#components'

// A player profile's column, shared by the site and the app.
const { apiFetch } = vi.hoisted(() => ({ apiFetch: vi.fn() }))
mockNuxtImport('useApiFetch', () => () => apiFetch)

afterEach(() => apiFetch.mockReset())

const PROFILE: ProfileResponse = {
  identity: { gameName: 'Faker', tagLine: 'KR1', platformId: 'KR', profileIconId: 6, summonerLevel: 700 },
  ranked: { tier: 'CHALLENGER', division: 'I', leaguePoints: 1204, wins: 120, losses: 80, winRate: 0.6 },
  mains: [],
  dedication: null,
  persona: null,
  positions: [{ position: 'MIDDLE', games: 180, rate: 0.9 }],
} as unknown as ProfileResponse

function mountAside(props: Record<string, unknown>, slots: Record<string, () => unknown> = {}) {
  apiFetch.mockResolvedValue({ entries: [] })
  return mountSuspended(defineComponent({
    setup: () => () => h(UApp, null, () => h(ProfileAside, {
      nameTag: 'Faker-KR1',
      profile: null,
      profileLoading: false,
      profileError: null,
      rankHistory: [],
      rankHistoryLoading: false,
      activity: null,
      activityLoading: false,
      champions: [],
      ...props,
    }, slots)),
  }))
}

describe('ProfileAside', () => {
  it('says once that the profile failed, instead of skeletons that never end', async () => {
    const wrapper = await mountAside({ profileError: new Error('boom') })

    expect(wrapper.text()).toContain('This player\'s profile could not be loaded')
    expect(wrapper.text()).not.toContain('Challenger')
  })

  it('draws the ranked card, the positions and the mains slot once the profile is in', async () => {
    const wrapper = await mountAside({ profile: PROFILE }, { mains: () => h('p', 'mains slot') })

    expect(wrapper.text()).toContain('Challenger')
    expect(wrapper.text()).toContain('mains slot')
    expect(apiFetch).toHaveBeenCalledWith('/ladder/KR/apex-cutoffs', expect.anything())
  })

  it('keeps the slot out while the profile loads', async () => {
    const wrapper = await mountAside({ profileLoading: true }, { mains: () => h('p', 'mains slot') })

    expect(wrapper.text()).not.toContain('mains slot')
  })
})

describe('ProfileFollowToggle', () => {
  it('follows the player under the region their platform belongs to', async () => {
    const wrapper = await mountSuspended(defineComponent({
      setup: () => () => h(UApp, null, () => h(ProfileFollowToggle, { identity: PROFILE.identity })),
    }))

    expect(wrapper.findComponent(FavoriteToggle).props()).toMatchObject({ gameName: 'Faker', tagLine: 'KR1', region: 'korea' })
  })
})

describe('useTruemainPlayerLabel', () => {
  it('reads the stored Riot ID, and the slug before the profile lands', async () => {
    const profile = ref<ProfileResponse | null>(null)
    let label!: Ref<string>
    await mountSuspended(defineComponent({
      setup() {
        label = useTruemainPlayerLabel('faker-kr1', profile)
        return () => h('div')
      },
    }))

    expect(label.value).toBe('faker#kr1')
    profile.value = PROFILE
    expect(label.value).toBe('Faker#KR1')
  })
})
