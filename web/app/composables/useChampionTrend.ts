import type { ChampionTrendResponse } from '~~/shared/types/champions'

/**
 * Per-patch winrate / pickrate series for the champion detail trend chart.
 *
 * Only the position filter is forwarded — the series intentionally spans the
 * recent patches the backend selects, so pinning a single `patch` would
 * collapse it to one point. Keyed on (champion, position) so it dedupes with
 * the rest of the detail page and re-fetches when the lane filter changes.
 * `server: false` mirrors `useChampion`: the detail page is client-rendered,
 * which keeps the chart (a `<ClientOnly>` consumer) free of hydration
 * mismatches.
 */
export function useChampionTrend(
  championId: MaybeRefOrGetter<number>,
  position: MaybeRefOrGetter<string | null | undefined>,
) {
  const championIdRef = computed(() => toValue(championId))
  const positionRef = computed(() => toValue(position) || undefined)

  return useLazyAsyncData<ChampionTrendResponse>(
    () => ['champion-trend', championIdRef.value, positionRef.value ?? ''].join('-'),
    () => $fetch<ChampionTrendResponse>(`/api/champions/${championIdRef.value}/trend`, {
      query: positionRef.value ? { position: positionRef.value } : {},
    }),
    { watch: [championIdRef, positionRef], server: false },
  )
}
