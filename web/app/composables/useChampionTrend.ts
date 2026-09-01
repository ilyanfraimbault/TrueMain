import type { ChampionTrendResponse } from '~~/shared/types/champions'
import { isLoadingStatus } from '~/utils/async-data'

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
 *
 * `enabled` gates the request: the detail page derives the lane from the
 * resolved champion, so it holds the fetch until that lands. Without the gate
 * the first render fires with an unresolved (null) position and a second
 * request fires the instant the champion's lane arrives — two calls per load.
 *
 * Bind skeletons to the returned `pending`, not to `status`: a closed gate
 * resolves an empty series straight to `success`, so `status` alone would read
 * "loaded, no points" for the whole champion fetch and the chart would flash
 * its no-data state before filling in. `pending` folds the gate in (and
 * supersedes Nuxt's own `pending`, which only knows about the request).
 */
export function useChampionTrend(
  championId: MaybeRefOrGetter<number>,
  position: MaybeRefOrGetter<string | null | undefined>,
  enabled: MaybeRefOrGetter<boolean> = true,
) {
  const championIdRef = computed(() => toValue(championId))
  const positionRef = computed(() => toValue(position) || undefined)
  const enabledRef = computed(() => toValue(enabled))

  const result = useLazyAsyncData<ChampionTrendResponse>(
    () => ['champion-trend', championIdRef.value, positionRef.value ?? ''].join('-'),
    () => {
      // Hold an empty series until the gate opens so we never fire a throwaway
      // request with a not-yet-resolved lane.
      if (!enabledRef.value) {
        return Promise.resolve({ championId: championIdRef.value, position: '', points: [] })
      }
      return $fetch<ChampionTrendResponse>(`/api/champions/${championIdRef.value}/trend`, {
        query: positionRef.value ? { position: positionRef.value } : {},
      })
    },
    { watch: [championIdRef, positionRef, enabledRef], server: false },
  )

  const pending = computed(() => !enabledRef.value || isLoadingStatus(result.status.value))

  return { ...result, pending }
}
