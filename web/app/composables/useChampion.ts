import type { ChampionResponse } from '~~/shared/types/champions'

type Filters = ReturnType<typeof useChampionFilters>['filters']

export interface UseChampionOptions {
  /**
   * When provided, the champion aggregate is scoped to a single player via
   * `GET /api/truemains/{nameTag}/champions/{id}` instead of the global
   * `GET /api/champions/{id}`. The read model is identical, so the rest of
   * the page is unchanged — only the data source differs.
   */
  nameTag?: MaybeRefOrGetter<string | undefined>
}

/**
 * Fetches the champion build page payload. Global by default; pass
 * `options.nameTag` to scope every aggregate to that player's games on the
 * champion (used by `/truemains/{nameTag}/champions/{id}`).
 *
 * For the player-scoped variant a 404 is meaningful — the account is unknown
 * or the player has too few games on the champion — so it surfaces as
 * `notEnoughData = true` (with `data` left null) instead of throwing, letting
 * the page render an empty state. The global variant keeps its historical
 * behaviour of retrying once without filters on a 404.
 */
export function useChampion(
  championId: MaybeRefOrGetter<number>,
  filters: Filters,
  options: UseChampionOptions = {},
) {
  const championIdRef = computed(() => toValue(championId))
  const nameTagRef = computed(() => {
    const value = toValue(options.nameTag)
    return value && value.length > 0 ? value : undefined
  })

  const notEnoughData = ref(false)

  const result = useLazyAsyncData<ChampionResponse | null>(
    () => {
      const f = filters.value
      return ['champion', nameTagRef.value ?? 'global', championIdRef.value, f.patch ?? '', f.position ?? ''].join('-')
    },
    async () => {
      const id = championIdRef.value
      const f = filters.value
      const nameTag = nameTagRef.value
      notEnoughData.value = false

      if (nameTag) {
        const response = await $fetch<ChampionResponse | null>(
          `/api/truemains/${encodeURIComponent(nameTag)}/champions/${id}`,
          { query: f, ignoreResponseError: true },
        )
        // The controller returns 404 (→ null body under `ignoreResponseError`)
        // for an unknown player or a champion below the min-games floor. The
        // only reliable "no data" tell is the absence of the championId field.
        if (!response || typeof response !== 'object' || typeof response.championId !== 'number') {
          notEnoughData.value = true
          return null
        }
        return response
      }

      try {
        return await $fetch<ChampionResponse>(`/api/champions/${id}`, { query: f })
      }
      catch (error: unknown) {
        const status = (error as { statusCode?: number }).statusCode
        const hadFilters = Boolean(f.patch || f.position)
        if (status === 404 && hadFilters) {
          return await $fetch<ChampionResponse>(`/api/champions/${id}`)
        }
        throw error
      }
    },
    { watch: [championIdRef, nameTagRef, filters], server: false },
  )

  return { ...result, notEnoughData }
}
