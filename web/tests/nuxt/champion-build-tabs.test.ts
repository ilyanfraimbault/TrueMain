import { flushPromises } from '@vue/test-utils'
import { mountSuspended, registerEndpoint } from '@nuxt/test-utils/runtime'
import { describe, expect, it, vi } from 'vitest'
import { ChampionBuildTabs, ChampionBuildTabsSkeleton, UApp } from '#components'
import type { ChampionResponse } from '~~/shared/types/champions'
import { PLACEHOLDER_BUILDS, PLACEHOLDER_CHAMPION_STATIC } from '~/utils/build-placeholder'
import { isLoadingStatus } from '~/utils/async-data'

const CHAMPION_ID = 266

const response: ChampionResponse = {
  championId: CHAMPION_ID,
  patch: '16.18',
  position: 'TOP',
  eloBracket: 'MASTER_PLUS',
  eloCoverage: 1,
  minSampleMet: true,
  totalGames: 1000,
  totalWins: 520,
  builds: [
    { ...PLACEHOLDER_BUILDS[0]!, pickRate: 0.42 },
    { ...PLACEHOLDER_BUILDS[1]!, pickRate: 0.31 },
  ],
}

// Held open by the test so the skeleton can be asserted before the API answers.
let releaseChampion!: () => void
const championReleased = new Promise<void>((resolve) => {
  releaseChampion = resolve
})
let championRequests = 0

registerEndpoint(`/api/champions/${CHAMPION_ID}`, async () => {
  championRequests++
  await championReleased
  return response
})

// The champion page's build section, reduced to the two branches it switches
// between — driven by the real `useChampion` (client-only `useLazyAsyncData`)
// so the test follows the same status transitions the page does.
const BuildSection = defineComponent({
  setup() {
    const { filters } = useChampionFilters()
    const { data: champion, status } = useChampion(CHAMPION_ID, filters)
    const loading = computed(() => isLoadingStatus(status.value))
    // `UApp` supplies the tooltip provider the icon tooltips inject, as `app.vue` does.
    return () =>
      h(UApp, null, () =>
        champion.value && !loading.value
          ? h(ChampionBuildTabs, {
              builds: champion.value.builds,
              championStatic: PLACEHOLDER_CHAMPION_STATIC,
              itemsMap: {},
              summonersMap: {},
              runeTree: null,
              championId: CHAMPION_ID,
            })
          : h(ChampionBuildTabsSkeleton))
  },
})

describe('champion build tabs', () => {
  it('renders the inert scaffolding until the build lands, then the real tabs', async () => {
    const wrapper = await mountSuspended(BuildSection)

    // Loading: the skeleton is the real tab component in `pending` mode — three
    // placeholder tabs, inert, with every pick rate masked.
    const skeleton = wrapper.find('[inert]')
    expect(skeleton.exists()).toBe(true)
    expect(skeleton.attributes('aria-hidden')).toBe('true')
    expect(wrapper.findAll('[role="tab"]')).toHaveLength(PLACEHOLDER_BUILDS.length)
    expect(wrapper.text()).not.toMatch(/\d+%/)

    // The client-only fetch is in flight, and the page is still on its skeleton.
    await vi.waitFor(() => expect(championRequests).toBe(1))
    await flushPromises()
    expect(wrapper.find('[inert]').exists()).toBe(true)

    releaseChampion()
    await vi.waitFor(() => expect(wrapper.find('[inert]').exists()).toBe(false))

    // Loaded: the same tab bar, now interactive and carrying the real numbers.
    expect(championRequests).toBe(1)
    const tabs = wrapper.findAll('[role="tab"]')
    expect(tabs).toHaveLength(2)
    expect(tabs[0]!.text()).toContain('42%')
    expect(tabs[1]!.text()).toContain('31%')
  })
})
