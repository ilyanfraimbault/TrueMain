import type { ChampionTimelineLeadsResponse } from '~~/shared/types/champions'

/**
 * Per-interval average lead vs the lane opponent for the champion detail page.
 *
 * Forwards both the position and the pinned patch — unlike the trend chart, the
 * leads slice is patch-scoped, so the active patch filter narrows it. Keyed on
 * (champion, position, patch) so it dedupes with the rest of the detail page and
 * re-fetches when either filter changes. `server: false` mirrors `useChampion`
 * (the detail page is client-rendered).
 *
 * `enabled` gates the request the same way `useChampionTrend` does: the page
 * derives the lane from the resolved champion, so the fetch holds until that
 * lands instead of firing once with a null lane and again the moment it arrives.
 */
export function useChampionTimelineLeads(
  championId: MaybeRefOrGetter<number>,
  position: MaybeRefOrGetter<string | null | undefined>,
  patch: MaybeRefOrGetter<string | null | undefined>,
  enabled: MaybeRefOrGetter<boolean> = true,
) {
  const championIdRef = computed(() => toValue(championId))
  const positionRef = computed(() => toValue(position) || undefined)
  const patchRef = computed(() => toValue(patch) || undefined)
  const enabledRef = computed(() => toValue(enabled))

  return useLazyAsyncData<ChampionTimelineLeadsResponse>(
    () => `champion-timeline-leads|${championIdRef.value}|${positionRef.value ?? ''}|${patchRef.value ?? ''}`,
    () => {
      if (!enabledRef.value || !positionRef.value) {
        return Promise.resolve({
          championId: championIdRef.value,
          position: positionRef.value ?? '',
          patch: patchRef.value ?? null,
          intervals: [],
        })
      }
      const query: Record<string, string> = { position: positionRef.value }
      if (patchRef.value) query.patch = patchRef.value
      return $fetch<ChampionTimelineLeadsResponse>(
        `/api/champions/${championIdRef.value}/timeline-leads`,
        { query },
      )
    },
    { watch: [championIdRef, positionRef, patchRef, enabledRef], server: false },
  )
}
