import type { ChampionStaticListItem } from '~~/shared/types/static-data'

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
export function useChampionStatic() {
  const { data, pending, error } = useFetch<ChampionStaticListItem[]>(
    '/api/static/champions',
    {
      server: false,
      key: 'static:champions',
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
