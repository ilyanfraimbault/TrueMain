import type { ChampionResponse } from '~~/shared/types/champions'

type Filters = ReturnType<typeof useChampionFilters>['filters']

export function useChampion(championId: MaybeRefOrGetter<number>, filters: Filters) {
  const championIdRef = computed(() => toValue(championId))

  return useLazyAsyncData<ChampionResponse>(
    () => {
      const f = filters.value
      return ['champion', championIdRef.value, f.patch ?? '', f.position ?? ''].join('-')
    },
    async () => {
      const id = championIdRef.value
      const f = filters.value
      try {
        return await $fetch<ChampionResponse>(`/api/champions/${id}`, { query: f })
      } catch (error: unknown) {
        const status = (error as { statusCode?: number }).statusCode
        const hadFilters = Boolean(f.patch || f.position)
        if (status === 404 && hadFilters) {
          return await $fetch<ChampionResponse>(`/api/champions/${id}`)
        }
        throw error
      }
    },
    { watch: [championIdRef, filters], server: false },
  )
}
