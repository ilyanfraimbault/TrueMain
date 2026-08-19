import type { ChampionOverviewResponse } from '~~/shared/types/champions'

/**
 * Homepage-sized champion snapshot (#972): the lifetime "games analyzed" total
 * plus a short, pre-sorted slice of the strongest rows — `GET /champions/overview`.
 * Client-only (`server: false`) with a homepage-own key, same rationale as
 * `home-champion-summaries` before it: the /champions page's cache key is
 * shaped for its own filter state, so sharing it would couple the two pages'
 * cache lifecycles for no gain.
 */
export function useChampionOverview() {
  return useLazyAsyncData<ChampionOverviewResponse>(
    'home-champion-overview',
    () => $fetch<ChampionOverviewResponse>('/api/champions/overview'),
    { server: false },
  )
}
