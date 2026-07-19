import type { ChampionStaticListItem } from '~~/shared/types/static-data'

/**
 * championId → static entry lookup for icon + name resolution — avoids a
 * linear scan per row when decorating API rows with the static champion list.
 */
export function useChampionsById(
  champions: MaybeRefOrGetter<ChampionStaticListItem[] | null | undefined>,
) {
  return computed(() => {
    const map = new Map<number, ChampionStaticListItem>()
    for (const champion of toValue(champions) ?? []) map.set(champion.championId, champion)
    return map
  })
}
