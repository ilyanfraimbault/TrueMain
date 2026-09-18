import type { ChampionSlugMap } from '~~/shared/types/static-data'
import { CHAMPION_SLUGS_STATE_KEY } from '~/composables/useChampionSlugs'

/**
 * Loads the `championId → slug` map into app-wide state before the first render
 * (#1124).
 *
 * Universal, not `.client`, and awaited on purpose: every page builds champion
 * links, and `/champions/{slug}` has to resolve its own param during SSR. A
 * lazily-filled map would mean the server rendered `/champions/103` links that
 * the client then rewrote to `/champions/ahri` — a hydration mismatch on almost
 * every page of the site.
 *
 * The cost is one Nitro-cached call per SSR render and ~2.5 kB of payload on
 * every page. That is the price of the slug scheme, and it is why this endpoint
 * carries ids and slugs *only* — see `server/api/static/champion-slugs.get.ts`.
 *
 * `useState` rather than the payload-cache trick `static-prefetch.client.ts`
 * uses: this value is app state, not a page's async data, and it must survive
 * every client-side navigation without re-fetching.
 */
export default defineNuxtPlugin(async (nuxtApp) => {
  const slugs = useState<ChampionSlugMap>(CHAMPION_SLUGS_STATE_KEY, () => ({}))

  if (import.meta.server) {
    // Best-effort, but note what "best-effort" costs on each side. For *link
    // building* an empty map is cheap: every builder falls back to the numeric
    // id, which still reaches the page. For *route resolution* it is not — a
    // slug has nothing to fall back to — so `championRouteAction` answers 503
    // rather than 404 while the map is empty, instead of asking search engines
    // to drop canonical URLs over a transient outage.
    slugs.value = await $fetch<ChampionSlugMap>('/api/static/champion-slugs').catch(() => ({}))
    return
  }

  // Client fallback for the case above — the SSR fetch failed and the payload
  // carries an empty map. Deliberately not awaited: links render numeric for a
  // moment and upgrade in place, which is a better failure than blocking
  // hydration on a second attempt at something already known to be down.
  if (Object.keys(slugs.value).length === 0) {
    nuxtApp.runWithContext(() => {
      void $fetch<ChampionSlugMap>('/api/static/champion-slugs')
        .then((data) => { slugs.value = data })
        .catch(() => {})
    })
  }
})
