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
  const nuxtApp = useNuxtApp()
  return useAsyncData<ChampionStaticData>(
    () => `champion-static-${toValue(championId)}-${toValue(patch) ?? 'none'}`,
    async () => {
      const id = toValue(championId)
      const resolvedPatch = toValue(patch) ?? ''
      const key = `champion-static-${id}-${resolvedPatch || 'none'}`
      const data = await $fetch<ChampionStaticData>(`/api/static/${id}`, {
        query: { patch: resolvedPatch },
      })
      markStaticFetched(key, nuxtApp)
      return data
    },
    {
      default: () => EMPTY_STATIC_DATA,
      getCachedData: key => getStaticCachedData(key, nuxtApp),
      watch: [() => toValue(championId), () => toValue(patch)],
    },
  )
}
