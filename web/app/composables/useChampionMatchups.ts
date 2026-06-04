import type { ChampionMatchups } from '~~/shared/types/champions'
import type { ChampionPosition } from '~/utils/positions'

export interface UseChampionMatchupsOptions {
  /** Patch to scope to (major.minor). Omit / null for all patches. */
  patch?: MaybeRefOrGetter<string | null | undefined>
  /**
   * When set, scope to a single player via
   * `GET /api/truemains/{nameTag}/champions/{id}/matchups` instead of the
   * global endpoint. Same contract, different pool.
   */
  nameTag?: MaybeRefOrGetter<string | undefined>
}

/**
 * Fetches every lane matchup for a champion at a position — the full list the
 * matchups table slices into best/worst and filters for the opponent search.
 * Global by default; pass `nameTag` to scope to one player. Fires once a
 * position is known. A 404 (unknown player) resolves to null so the table can
 * render an empty state rather than an error.
 */
export function useChampionMatchups(
  championId: MaybeRefOrGetter<number>,
  position: MaybeRefOrGetter<ChampionPosition | null>,
  options: UseChampionMatchupsOptions = {},
) {
  const championIdRef = computed(() => toValue(championId))
  const positionRef = computed(() => toValue(position))
  const patchRef = computed(() => {
    const value = toValue(options.patch)
    return value && value.length > 0 ? value : undefined
  })
  const nameTagRef = computed(() => {
    const value = toValue(options.nameTag)
    return value && value.length > 0 ? value : undefined
  })

  return useLazyAsyncData<ChampionMatchups | null>(
    () => [
      'champion-matchups',
      nameTagRef.value ?? 'global',
      championIdRef.value,
      positionRef.value ?? '',
      patchRef.value ?? '',
    ].join('-'),
    async () => {
      const position = positionRef.value
      if (!position) return null

      const query: Record<string, string> = { position }
      if (patchRef.value) query.patch = patchRef.value

      const nameTag = nameTagRef.value
      const path = nameTag
        ? `/api/truemains/${encodeURIComponent(nameTag)}/champions/${championIdRef.value}/matchups`
        : `/api/champions/${championIdRef.value}/matchups`

      try {
        return await $fetch<ChampionMatchups>(path, { query })
      }
      catch (error: unknown) {
        const status = (error as { statusCode?: number, response?: { status?: number } }).statusCode
          ?? (error as { response?: { status?: number } }).response?.status
        // Unknown player (player-scoped route) → empty state, not an error.
        if (status === 404) return null
        throw error
      }
    },
    {
      watch: [championIdRef, positionRef, patchRef, nameTagRef],
      server: false,
    },
  )
}
