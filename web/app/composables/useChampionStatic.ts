import type { ChampionStaticData } from '~~/shared/types/static-data'

const EMPTY_STATIC_DATA: ChampionStaticData = {
  championName: null,
  championIconUrl: null,
  championSpells: {},
  partype: '',
}

export function useChampionStatic(
  championId: MaybeRefOrGetter<number>,
  patch: MaybeRefOrGetter<string | null>,
) {
  return useFetch<ChampionStaticData>(
    () => `/api/static/${toValue(championId)}`,
    {
      key: () => `champion-static-${toValue(championId)}-${toValue(patch) ?? 'none'}`,
      query: computed(() => ({ patch: toValue(patch) ?? '' })),
      default: () => EMPTY_STATIC_DATA,
    },
  )
}
