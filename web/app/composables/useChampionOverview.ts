import type { ChampionOverviewResponse } from '~~/shared/types/champions'

/**
 * Homepage-sized champion snapshot (#972): the lifetime "games analyzed" total
 * plus a short, pre-sorted slice of the strongest rows — `GET /champions/overview`.
 * Client-only (`server: false`) with a homepage-own key, same rationale as
 * `home-champion-summaries` before it: the /champions page's cache key is
 * shaped for its own filter state, so sharing it would couple the two pages'
 * cache lifecycles for no gain.
 *
 * Not lazy: awaited, it settles once the fetch has, so the homepage can hold a
 * client-side navigation under the loading bar until it is in (#1689). A hard
 * load still resolves it at once — `server: false` defers the fetch past
 * hydration — and keeps its skeleton.
 */
export function useChampionOverview() {
  return useApi<ChampionOverviewResponse>('/champions/overview', {
    key: 'home-champion-overview',
    server: false,
  })
}
