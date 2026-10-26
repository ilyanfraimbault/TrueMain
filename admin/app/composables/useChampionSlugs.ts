import type { ChampionSlugMap } from '#site-shared/types/static-data'

/**
 * `championId → url slug` from `/api/static/champion-slugs`, the map the public
 * site's router and links resolve (`web/layers/nitro`, #1124), read here to link a
 * champion to its page on the site (#2291). Kept in the shared cache like the
 * champion list: a champion's slug never moves between two navigations. Until it
 * lands (or when it fails) the links fall back to the numeric id, which the site
 * redirects.
 */
const CHAMPION_SLUGS_KEY = 'static:champion-slugs'

export function useChampionSlugs() {
  const { data } = useFetch<ChampionSlugMap>('/api/static/champion-slugs', {
    key: CHAMPION_SLUGS_KEY,
    getCachedData: (key, _nuxtApp, ctx) =>
      ctx.cause === 'initial' ? opsCache.get(key)?.value as ChampionSlugMap | undefined : undefined,
    onResponse: ({ response }) => {
      if (response.ok) {
        opsCache.set(CHAMPION_SLUGS_KEY, response._data)
      }
    },
  })
  return { slugs: data }
}
