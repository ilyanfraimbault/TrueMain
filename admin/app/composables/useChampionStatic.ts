import type { ChampionStaticListItem } from '#site-shared/types/static-data'

export interface ChampionStaticEntry {
  name: string
  iconUrl: string
}

/**
 * Load the DDragon champion list (`/api/static/champions`) and expose a
 * `championId -> { name, iconUrl }` lookup. The endpoint is cached server-side
 * per patch, so this is cheap to share across panels. We don't pass a `patch`
 * param — the latest DDragon patch is fine for resolving names/icons, which are
 * stable across recent patches.
 *
 * `nameFor` falls back to `Champion {id}` so an unmapped id (new champion not
 * yet on the resolved patch) still renders something honest rather than blank.
 */
export const STATIC_CHAMPIONS_KEY = 'static:champions'

export function useChampionStatic() {
  const { data, pending, error } = useFetch<ChampionStaticListItem[]>(
    '/api/static/champions',
    {
      // No `server` option: the whole admin app is `ssr: false`
      // (`nuxt.config.ts`), so every fetch already runs client-side.
      key: STATIC_CHAMPIONS_KEY,
      // Names and icons do not move between two navigations, so a stored list is
      // drawn without revalidating (#2306) until the shared cache's TTL drops it.
      getCachedData: (key, _nuxtApp, ctx) =>
        ctx.cause === 'initial' ? opsCache.get(key)?.value as ChampionStaticListItem[] | undefined : undefined,
      onResponse: ({ response }) => {
        if (response.ok) {
          opsCache.set(STATIC_CHAMPIONS_KEY, response._data)
        }
      },
    },
  )

  const byId = computed(() => {
    const map = new Map<number, ChampionStaticEntry>()
    for (const champ of data.value ?? []) {
      map.set(champ.championId, { name: champ.name, iconUrl: champ.iconUrl })
    }
    return map
  })

  function nameFor(championId: number): string {
    return byId.value.get(championId)?.name ?? `Champion ${championId}`
  }

  function iconFor(championId: number): string | null {
    return byId.value.get(championId)?.iconUrl ?? null
  }

  return { byId, nameFor, iconFor, pending, error }
}
