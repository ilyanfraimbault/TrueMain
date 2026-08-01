import type { PlayerChampionPerformanceResponse } from '~~/shared/types/performance'
import type { ChampionPosition } from '~/utils/positions'
import { fetchErrorStatus } from '~/utils/errors'

export interface UsePlayerChampionPerformanceOptions {
  /** Patch to pin; omitted means every patch. */
  patch?: MaybeRefOrGetter<string | null | undefined>
  /** Lane to pin; omitted means every lane. */
  position?: MaybeRefOrGetter<ChampionPosition | null | undefined>
}

/**
 * Fetches the aggregate performance score for a player on a champion — the
 * average of TrueMain's per-match score over their recent ranked games, with
 * the per-component breakdown behind it (see docs/performance-score.md).
 *
 * Client-only and below the fold, like the matchups sidebar and the "vs mains"
 * card. A 404 means the account is unknown, which is an empty state rather than
 * an error, so it resolves to `null`; a player whose sample is merely thin comes
 * back as a 200 with `games` below `minGames` and every average null, which the
 * card renders as its own honest notice. Every other status propagates.
 */
export function usePlayerChampionPerformance(
  nameTag: MaybeRefOrGetter<string>,
  championId: MaybeRefOrGetter<number>,
  options: UsePlayerChampionPerformanceOptions = {},
) {
  const nameTagRef = computed(() => toValue(nameTag))
  const championIdRef = computed(() => toValue(championId))
  const patchRef = computed(() => toValue(options.patch) || undefined)
  const positionRef = computed(() => toValue(options.position) || undefined)

  return useLazyAsyncData<PlayerChampionPerformanceResponse | null>(
    () => [
      'player-champion-performance',
      nameTagRef.value,
      championIdRef.value,
      patchRef.value ?? '',
      positionRef.value ?? '',
    ].join('-'),
    async () => {
      if (!nameTagRef.value) return null

      const query: Record<string, string> = {}
      if (patchRef.value) query.patch = patchRef.value
      if (positionRef.value) query.position = positionRef.value

      try {
        return await $fetch<PlayerChampionPerformanceResponse>(
          `/api/truemains/${encodeURIComponent(nameTagRef.value)}/champions/${championIdRef.value}/performance`,
          { query },
        )
      }
      catch (error: unknown) {
        if (fetchErrorStatus(error) === 404) return null
        throw error
      }
    },
    {
      watch: [nameTagRef, championIdRef, patchRef, positionRef],
      server: false,
    },
  )
}
