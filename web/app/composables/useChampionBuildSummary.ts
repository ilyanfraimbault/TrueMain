import type { ChampionBuildSummary } from '~~/shared/types/champion-build-summary'
import { championSliceQuery } from '#common/utils/champion-slice-query'

type ChampionFilters = ReturnType<typeof useChampionFilters>['filters']

/**
 * The build in words, server-rendered (#1123) — the one piece of build content
 * on the champion page that reaches the HTML before JS runs. Everything else on
 * that page is `server: false`, so a crawler used to receive a shell under a
 * title promising a build.
 *
 * Not a hydration risk, and specifically not #149's: that was a *client-only*
 * fetch racing SSR and winning, so the server rendered content while the
 * client's first render started in its loading state. This one is SSR-enabled
 * and its result travels in the Nuxt payload, so the client's hydration render
 * reads the same object the server rendered from — the two agree by
 * construction. The interactive panels stay client-only exactly as they were.
 *
 * Keyed on the **URL** filters, not on `selectedPatch`/`selectedPosition`:
 * those reconcile to the aggregate's resolved values once the client-only
 * champion fetch lands, which would change the key after hydration and cost a
 * second round trip (plus a visible re-render) on every load. The URL filters
 * are identical on the server and at hydration, and the endpoint resolves the
 * same defaults the aggregate does, so both describe the same slice.
 *
 * Returns the `useAsyncData` result un-awaited: the page awaits it together
 * with the champion fetch — for the HTML on the server, under the loading bar
 * on a client-side navigation (#1689). `useApiFetch` forwards the visitor
 * during SSR (#1557).
 */
export function useChampionBuildSummary(championId: ComputedRef<number>, filters: ChampionFilters) {
  const apiFetch = useApiFetch()
  return useAsyncData(
    () => [
      'champion-build-summary',
      championId.value,
      filters.value.patch ?? '',
      filters.value.position ?? '',
      filters.value.eloBracket ?? '',
      filters.value.opponentChampionId ?? '',
      // The population belongs in the key like every other slice dimension: the
      // request below carries it, and two populations sharing one entry means one
      // gets served under the other's filter. `watch: [championId, filters]` masks
      // it in the live path (a fresh object every recompute forces a refetch), but
      // the key is what SSR payload reuse keys on.
      filters.value.truemainsOnly ? 'truemains' : 'everyone',
    ].join('-'),
    (_nuxtApp, { signal }) => apiFetch<ChampionBuildSummary>(`/champion-summary/${championId.value}`, {
      // The whole slice, matchup and population included: without them the prose
      // describes another build than the panels right above it (#923, #1346).
      query: championSliceQuery(filters.value),
      signal,
    }),
    {
      watch: [championId, filters],
      // `default` rather than letting `data` start as `undefined`: "not fetched
      // yet", "the fetch failed" and "the slice has nothing to say" are one state
      // for this block — it renders nothing — so giving them one value keeps the
      // component from having to distinguish three nothings.
      default: () => null,
    },
  )
}
