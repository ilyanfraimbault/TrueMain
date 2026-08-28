/** Cache key of the version list — shared by every page, hence the fixed key. */
export const DDRAGON_VERSIONS_KEY = 'ddragon-versions'

/**
 * DDragon's version list, newest first. `[0]` is the latest patch, which is
 * what most callers want; the patch pickers read the whole list.
 *
 * Goes through `/api/static/versions`, not the CDN (#1231). It used to `$fetch`
 * `ddragon.leagueoflegends.com` straight from the browser — the only front-end
 * call that did — on nearly every page, so nothing was shared between visitors
 * and a DDragon outage was visible to them rather than being a server-side
 * cache miss. Behind the proxy it is a 1 h Nitro entry like every other
 * `/api/static/*` lookup, plus the same 1 h client-side TTL
 * (`getStaticCachedData`) so a remount on the next page reuses the payload
 * instead of re-issuing the request.
 *
 * Client-only (`server: false`): every consumer feeds it into a client-only
 * fetch's patch key or into an icon URL, so SSR-ing it would add payload
 * without moving anything above the fold. `default: []` keeps `data` an array
 * during an outage — `resolveChampionStaticPatch` is written against that.
 */
export function useDDragonVersions() {
  const nuxtApp = useNuxtApp()
  return useLazyAsyncData<string[]>(
    DDRAGON_VERSIONS_KEY,
    async () => {
      const data = await $fetch<string[]>('/api/static/versions')
      markStaticFetched(DDRAGON_VERSIONS_KEY, nuxtApp)
      return data
    },
    {
      default: () => [],
      server: false,
      getCachedData: key => getStaticCachedData(key, nuxtApp),
    },
  )
}
