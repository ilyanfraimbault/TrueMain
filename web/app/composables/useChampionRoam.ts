import type { ChampionRoamResponse } from '~~/shared/types/champions'

/**
 * Roam metric (out-of-lane kill-participation share) for the champion detail page
 * (issue #536). Forwards position + the pinned patch, keyed on
 * (champion, position, patch) so it dedupes with the rest of the page and
 * re-fetches when either filter changes. `server: false` mirrors `useChampion`;
 * `enabled` gates the request until the champion (and its lane) resolves.
 */
export function useChampionRoam(
  championId: MaybeRefOrGetter<number>,
  position: MaybeRefOrGetter<string | null | undefined>,
  patch: MaybeRefOrGetter<string | null | undefined>,
  enabled: MaybeRefOrGetter<boolean> = true,
) {
  const championIdRef = computed(() => toValue(championId))
  const positionRef = computed(() => toValue(position) || undefined)
  const patchRef = computed(() => toValue(patch) || undefined)
  const enabledRef = computed(() => toValue(enabled))

  return useLazyAsyncData<ChampionRoamResponse>(
    () => `champion-roam|${championIdRef.value}|${positionRef.value ?? ''}|${patchRef.value ?? ''}`,
    () => {
      if (!enabledRef.value || !positionRef.value) {
        return Promise.resolve({
          championId: championIdRef.value,
          position: positionRef.value ?? '',
          patch: patchRef.value ?? null,
          games: 0,
          killParticipations: 0,
          outOfLaneParticipations: 0,
          outOfLaneShare: null,
        })
      }
      const query: Record<string, string> = { position: positionRef.value }
      if (patchRef.value) query.patch = patchRef.value
      return $fetch<ChampionRoamResponse>(
        `/api/champions/${championIdRef.value}/roam`,
        { query },
      )
    },
    { watch: [championIdRef, positionRef, patchRef, enabledRef], server: false },
  )
}
