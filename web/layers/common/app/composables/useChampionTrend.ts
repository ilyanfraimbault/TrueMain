import type { ChampionTrendResponse } from '#shared/types/champions'
import { isLoadingStatus } from '#common/utils/async-data'

/**
 * Per-patch winrate / pickrate series for the champion detail trend chart.
 *
 * The position, the rank filter and the population are forwarded, never the
 * patch — the series intentionally spans the recent patches the backend
 * selects, so pinning a single `patch` would collapse it to one point. The rank
 * and population make the banner's moves and the chart describe the page's
 * slice (#2353); omitted, the API reads every rank and mains only (the desktop
 * app's slice). Keyed on all four so it re-fetches when any of them changes.
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
/** The trend's cache key: one entry per champion, lane, rank filter and population. */
export function championTrendKey(
  championId: number,
  position: string | undefined,
  eloBracket: string | undefined,
  everyone: boolean,
): string {
  return ['champion-trend', championId, position ?? '', eloBracket ?? '', everyone ? 'everyone' : 'truemains'].join('-')
}

export function useChampionTrend(
  championId: MaybeRefOrGetter<number>,
  position: MaybeRefOrGetter<string | null | undefined>,
  enabled: MaybeRefOrGetter<boolean> = true,
  slice: {
    eloBracket?: MaybeRefOrGetter<string | null | undefined>
    truemainsOnly?: MaybeRefOrGetter<boolean | undefined>
  } = {},
) {
  const championIdRef = computed(() => toValue(championId))
  const positionRef = computed(() => toValue(position) || undefined)
  const enabledRef = computed(() => toValue(enabled))
  const eloBracketRef = computed(() => toValue(slice.eloBracket) || undefined)
  // Mains only is the API's default, so only the wider population is sent.
  const everyoneRef = computed(() => toValue(slice.truemainsOnly) === false)

  const apiFetch = useApiFetch()

  const result = useLazyAsyncData<ChampionTrendResponse>(
    () => championTrendKey(championIdRef.value, positionRef.value, eloBracketRef.value, everyoneRef.value),
    (_nuxtApp, { signal }) => {
      // Hold an empty series until the gate opens so we never fire a throwaway
      // request with a not-yet-resolved lane.
      if (!enabledRef.value) {
        return Promise.resolve({ championId: championIdRef.value, position: '', points: [] })
      }
      return apiFetch<ChampionTrendResponse>(`/champions/${championIdRef.value}/trend`, {
        query: {
          ...(positionRef.value ? { position: positionRef.value } : {}),
          ...(eloBracketRef.value ? { eloBracket: eloBracketRef.value } : {}),
          ...(everyoneRef.value ? { truemainsOnly: 'false' } : {}),
        },
        signal,
      })
    },
    { watch: [championIdRef, positionRef, enabledRef, eloBracketRef, everyoneRef], server: false },
  )

  const pending = computed(() => !enabledRef.value || isLoadingStatus(result.status.value))

  return { ...result, pending }
}
