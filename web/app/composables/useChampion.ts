import type { ChampionResponse } from '~~/shared/types/champions'

type Filters = ReturnType<typeof useChampionFilters>['filters']

export function useChampion(championId: MaybeRefOrGetter<number>, filters: Filters) {
  const championIdRef = computed(() => toValue(championId))

  return useAsyncData<ChampionResponse>(
    () => {
      const f = filters.value
      return [
        'champion', championIdRef.value,
        f.patch ?? '', f.position ?? '', f.platformId ?? '',
        f.riotAccountId ?? '', f.buildId ?? '',
      ].join('-')
    },
    async () => {
      const id = championIdRef.value
      const f = filters.value
      const baseQuery = { maxDepth: f.maxDepth, minBranchGames: f.minBranchGames }
      try {
        return await $fetch<ChampionResponse>(`/api/champions/${id}`, { query: f })
      } catch (error: unknown) {
        const status = (error as { statusCode?: number }).statusCode
        const hadFilters = Boolean(f.patch || f.position || f.platformId || f.riotAccountId || f.buildId)
        // 404 with filters likely means "no data for that filter combo".
        // Fall back to the unfiltered champion so the page still renders
        // basic info instead of surfacing a hard error.
        if (status === 404 && hadFilters) {
          return await $fetch<ChampionResponse>(`/api/champions/${id}`, { query: baseQuery })
        }
        throw error
      }
    },
    { watch: [championIdRef, filters] },
  )
}
