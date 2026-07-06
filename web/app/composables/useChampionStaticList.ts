import type { ChampionStaticListItem } from '~~/shared/types/static-data'

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
 * No `default` on purpose: matches the other `champion-static-list` callsites,
 * so `data` is `ChampionStaticListItem[] | null` — guard with `?? []`.
 */
export function useChampionStaticList() {
  const nuxtApp = useNuxtApp()
  return useLazyAsyncData<ChampionStaticListItem[]>(
    'champion-static-list',
    async () => {
      const data = await $fetch<ChampionStaticListItem[]>('/api/static/champions')
      markStaticFetched('champion-static-list', nuxtApp)
      return data
    },
    { getCachedData: key => getStaticCachedData(key, nuxtApp), server: false },
  )
}
