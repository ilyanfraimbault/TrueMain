import type { ChampionPowerspikesResponse } from '~~/shared/types/champions'

/**
 * Power curve + event spikes for the champion detail page (issue #571).
 * Forwards position + the pinned patch (the slice is patch-scoped), keyed on
 * (champion, position, patch) so it dedupes with the rest of the page and
 * re-fetches when either filter changes. `server: false` mirrors `useChampion`;
 * `enabled` gates the request until the champion (and its lane) resolves.
 */
export function useChampionPowerspikes(
  championId: MaybeRefOrGetter<number>,
  position: MaybeRefOrGetter<string | null | undefined>,
  patch: MaybeRefOrGetter<string | null | undefined>,
  enabled: MaybeRefOrGetter<boolean> = true,
) {
  const championIdRef = computed(() => toValue(championId))
  const positionRef = computed(() => toValue(position) || undefined)
  const patchRef = computed(() => toValue(patch) || undefined)
  const enabledRef = computed(() => toValue(enabled))

  return useLazyAsyncData<ChampionPowerspikesResponse>(
    () => `champion-powerspikes|${championIdRef.value}|${positionRef.value ?? ''}|${patchRef.value ?? ''}`,
    () => {
      if (!enabledRef.value || !positionRef.value) {
        return Promise.resolve({
          championId: championIdRef.value,
          position: positionRef.value ?? '',
          patch: patchRef.value ?? null,
          curve: [],
          events: [],
        })
      }
      const query: Record<string, string> = { position: positionRef.value }
      if (patchRef.value) query.patch = patchRef.value
      return $fetch<ChampionPowerspikesResponse>(
        `/api/champions/${championIdRef.value}/powerspikes`,
        { query },
      )
    },
    { watch: [championIdRef, positionRef, patchRef, enabledRef], server: false },
  )
}
