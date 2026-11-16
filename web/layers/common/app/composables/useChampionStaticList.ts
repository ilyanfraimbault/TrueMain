import type { ChampionStaticListItem } from '#shared/types/static-data'

/**
 * Shared static champion list (id, name, icon) for the active patch.
 *
 * Backs the unified search (champions group) and the home/champions pages. The
 * cache key and options MUST stay identical across every callsite — Nuxt warns
 * when a shared `useAsyncData` key is reused with diverging options — so they
 * live here once. The `static-prefetch.client.ts` plugin warms this same key at
 * boot, so `getCachedData` lets consumers reuse the payload instead of
 * refetching (notably the header search, which mounts on every page).
 *
 * On the server too (#2425): the directory and the tier list print names and portraits
 * in their first HTML, and the list is a few KB once compressed.
 *
 * No `default` on purpose: matches the other `champion-static-list` callsites,
 * so `data` is `ChampionStaticListItem[] | null` — guard with `?? []`.
 */
/** The list's `useAsyncData` key, which the boot prefetch (`static-prefetch.client.ts`) warms. */
export const CHAMPION_STATIC_LIST_KEY = 'champion-static-list'

export function useChampionStaticList() {
  const nuxtApp = useNuxtApp()
  const apiFetch = useApiFetch()
  return useLazyAsyncData<ChampionStaticListItem[]>(
    CHAMPION_STATIC_LIST_KEY,
    async (_nuxtApp, { signal }) => {
      const data = await apiFetch<ChampionStaticListItem[]>('/static/champions', { signal })
      markStaticFetched(CHAMPION_STATIC_LIST_KEY, nuxtApp)
      return data
    },
    { getCachedData: key => getStaticCachedData(key, nuxtApp) },
  )
}
