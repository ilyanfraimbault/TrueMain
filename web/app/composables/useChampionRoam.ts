import type { ChampionRoamResponse } from '~~/shared/types/champions'

/**
 * Roam metric (per-game out-of-lane kill participations at @5/@10/@15) for the
 * champion detail page (issue #536). Forwards position + the pinned patch, keyed on
 * (champion, position, patch) so it dedupes with the rest of the page and
 * re-fetches when either filter changes. `server: false` mirrors `useChampion`;
 * `enabled` gates the request until the champion (and its lane) resolves.
 */
export function useChampionRoam(
  championId: MaybeRefOrGetter<number>,
  position: MaybeRefOrGetter<string | null | undefined>,
  patch: MaybeRefOrGetter<string | null | undefined>,
  enabled: MaybeRefOrGetter<boolean> = true,
  eloBracket: MaybeRefOrGetter<string | null | undefined> = undefined,
) {
  const championIdRef = computed(() => toValue(championId))
  const positionRef = computed(() => toValue(position) || undefined)
  const patchRef = computed(() => toValue(patch) || undefined)
  const enabledRef = computed(() => toValue(enabled))
  const eloBracketRef = computed(() => toValue(eloBracket) || undefined)

  return useLazyAsyncData<ChampionRoamResponse>(
    () => `champion-roam|${championIdRef.value}|${positionRef.value ?? ''}|${patchRef.value ?? ''}|${eloBracketRef.value ?? ''}`,
    () => {
      if (!enabledRef.value || !positionRef.value) {
        return Promise.resolve({
          championId: championIdRef.value,
          position: positionRef.value ?? '',
          patch: patchRef.value ?? null,
          games: 0,
          roamKp5: null,
          roamKp10: null,
          roamKp15: null,
        })
      }
      const query: Record<string, string> = { position: positionRef.value }
      if (patchRef.value) query.patch = patchRef.value
      if (eloBracketRef.value) query.eloBracket = eloBracketRef.value
      return $fetch<ChampionRoamResponse>(
        `/api/champions/${championIdRef.value}/roam`,
        { query },
      )
    },
    { watch: [championIdRef, positionRef, patchRef, enabledRef, eloBracketRef], server: false },
  )
}
