import type { PlayerBuildDivergenceResponse } from '~~/shared/types/divergence'
import type { ChampionPosition } from '~/utils/positions'
import { fetchErrorStatus } from '~/utils/errors'

export interface UsePlayerBuildDivergenceOptions {
  /** Patch to pin; omitted means the player's most recent usable patch. */
  patch?: MaybeRefOrGetter<string | null | undefined>
  /** Position to pin; omitted means the player's dominant lane on the champion. */
  position?: MaybeRefOrGetter<ChampionPosition | null | undefined>
}

/**
 * Fetches the "you vs mains" comparison for a player on a champion. Client-only
 * and below the fold, like the matchups sidebar.
 *
 * A 404 means the account is unknown or we hold no aggregate at all for them on
 * the champion — an empty state, not an error, so it resolves to `null`. A
 * player whose sample is merely thin comes back as a 200 with `minSampleMet`
 * false and no dimensions, which the card renders as its own honest notice.
 * Every other status propagates so the caller can surface a real failure.
 */
export function usePlayerBuildDivergence(
  nameTag: MaybeRefOrGetter<string>,
  championId: MaybeRefOrGetter<number>,
  options: UsePlayerBuildDivergenceOptions = {},
) {
  const nameTagRef = computed(() => toValue(nameTag))
  const championIdRef = computed(() => toValue(championId))
  const patchRef = computed(() => toValue(options.patch) || undefined)
  const positionRef = computed(() => toValue(options.position) || undefined)

  return useLazyAsyncData<PlayerBuildDivergenceResponse | null>(
    () => [
      'player-build-divergence',
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
        return await $fetch<PlayerBuildDivergenceResponse>(
          `/api/truemains/${encodeURIComponent(nameTagRef.value)}/champions/${championIdRef.value}/divergence`,
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
