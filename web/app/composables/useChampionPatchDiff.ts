import type { ChampionPatchDiffResponse } from '~~/shared/types/champions'

/**
 * Per-champion patch-diff slice for the champion detail page (issue #534).
 *
 * Forwards the position plus the two patches to compare. Either patch may be
 * left undefined — the backend then defaults to the two most recent patches
 * with data for the resolved lane, so the section opens on the latest
 * patch-over-patch change. Keyed on (champion, position, from, to) so it
 * dedupes with the rest of the detail page and re-fetches when any selector
 * changes. `server: false` mirrors `useChampion`. `enabled` gates the request
 * until the champion (and its lane) resolves, the same way the trend and
 * scaling composables do, so the first request fires once with the resolved
 * lane instead of twice.
 */
export function useChampionPatchDiff(
  championId: MaybeRefOrGetter<number>,
  position: MaybeRefOrGetter<string | null | undefined>,
  from: MaybeRefOrGetter<string | null | undefined>,
  to: MaybeRefOrGetter<string | null | undefined>,
  enabled: MaybeRefOrGetter<boolean> = true,
) {
  const championIdRef = computed(() => toValue(championId))
  const positionRef = computed(() => toValue(position) || undefined)
  const fromRef = computed(() => toValue(from) || undefined)
  const toRef = computed(() => toValue(to) || undefined)
  const enabledRef = computed(() => toValue(enabled))

  return useLazyAsyncData<ChampionPatchDiffResponse>(
    () => [
      'champion-patch-diff',
      championIdRef.value,
      positionRef.value ?? '',
      fromRef.value ?? '',
      toRef.value ?? '',
    ].join('|'),
    () => {
      // Hold an empty model until the gate opens so we never fire a throwaway
      // request with a not-yet-resolved lane.
      if (!enabledRef.value) {
        return Promise.resolve({
          championId: championIdRef.value,
          position: positionRef.value ?? '',
          availablePatchCount: 0,
          from: null,
          to: null,
          delta: null,
        })
      }
      const query: Record<string, string> = {}
      if (positionRef.value) query.position = positionRef.value
      if (fromRef.value) query.from = fromRef.value
      if (toRef.value) query.to = toRef.value
      return $fetch<ChampionPatchDiffResponse>(
        `/api/champions/${championIdRef.value}/patch-diff`,
        { query },
      )
    },
    { watch: [championIdRef, positionRef, fromRef, toRef, enabledRef], server: false },
  )
}
