import type { MaybeRefOrGetter } from 'vue'
import type {
  AggregationsResponse,
  ChampionStatsFilters,
  ChampionStatsRow,
  DbStorageHistory,
  DbTableRow,
  IngestionTimeGranularity,
  MatchesIngested,
  MatchTimeBucket,
  MatchTimeGranularity,
  OverviewStats,
  PatchCoverageResponse,
} from '~~/shared/types/ops'

// The ops-API fetch wrapper, plus the stats and database reads. The other areas
// live next to it, one file each: `useOpsPipeline.ts` (process runs, logs, crashes,
// Riot usage, health, configuration), `useOpsDataQuality.ts` and `useOpsAccounts.ts`
// (seed requests, candidates, account explorer).

/**
 * Thin wrapper around `useFetch('/api/ops' + path)` — the browser-facing,
 * session-authenticated proxy to the backend ops API. Returns the `useFetch`
 * shape (`data`, `pending`, `error`, `refresh`, `status`) plus `refreshing`.
 *
 * Stale-while-revalidate across navigations (#2306, `utils/ops-cache.ts`): a
 * panel returning to a key fetched in the last few minutes starts from the stored
 * payload and revalidates behind it. Hence the one departure from `useFetch`:
 * **`pending` means "nothing to draw yet"** — a request in flight with no payload
 * on screen, the skeleton's condition — while `refreshing` is any request in
 * flight, what a refresh button's spinner shows. A panel that writes
 * `v-if="pending"` for its skeleton therefore keeps the cached content instead of
 * blanking it on every visit.
 *
 * `query` may be a getter/ref so callers can pass reactive filters; `useFetch`
 * watches it and re-fetches when it changes. Every ops fetch runs client-side:
 * the whole app is `ssr: false` (`nuxt.config.ts`) because the dashboard is
 * gated behind an operator session and the data is operational, not
 * SEO-relevant. That is an application-level decision, so no per-request
 * `server` option is needed here.
 */
export function useOps<T>(
  path: string,
  query?: MaybeRefOrGetter<Record<string, string | number | boolean | undefined>>,
) {
  const queryParams = query
    ? computed(() => cleanQuery(toValue(query)))
    : undefined
  // Distinct per (path, query), see `opsCacheKey`.
  const key = queryParams
    ? computed(() => opsCacheKey(path, queryParams.value))
    : opsCacheKey(path)
  const { loggedIn } = useUserSession()
  const cacheOptions = opsCacheFetchOptions(key, () => loggedIn.value)
  const fetched = useFetch<T>(`/api/ops${path}`, {
    query: queryParams,
    key,
    onRequest: cacheOptions.onRequest,
    onResponse: cacheOptions.onResponse,
    // Both cast: `useFetch`'s overloads resolve its data type through conditional
    // types a generic `T` cannot satisfy, and a typed `default` would widen `data`
    // past `T | undefined`. At runtime each returns a payload stored for this key.
    default: cacheOptions.default as () => undefined,
    getCachedData: cacheOptions.getCachedData as never,
  })
  trackOpsCacheShown(key)

  const refreshing = computed(() => fetched.status.value === 'pending')
  const pending = computed(() =>
    refreshing.value && (fetched.data.value === undefined || fetched.data.value === null),
  )
  return { ...fetched, pending, refreshing }
}

/** `GET /api/ops/stats/overview` — site-wide totals for the Overview panel. */
export function useOverviewStats() {
  return useOps<OverviewStats>('/stats/overview')
}

/**
 * `GET /api/ops/stats/champions` — per-champion games/mains/otps, optionally
 * filtered. Pass a reactive getter so the table/charts re-fetch on filter
 * change.
 */
export function useChampionStats(
  filters?: MaybeRefOrGetter<ChampionStatsFilters>,
) {
  return useOps<ChampionStatsRow[]>(
    '/stats/champions',
    filters ? () => ({ ...toValue(filters) }) : undefined,
  )
}

/**
 * `GET /api/ops/stats/matches-over-time` — match counts bucketed by game date at
 * the given granularity (day/week/month/year/patch), returned chronologically. Pass a
 * reactive ref/getter so the chart re-fetches when the granularity changes.
 */
export function useMatchesOverTime(
  granularity: MaybeRefOrGetter<MatchTimeGranularity>,
) {
  return useOps<MatchTimeBucket[]>(
    '/stats/matches-over-time',
    () => ({ granularity: toValue(granularity) }),
  )
}

/**
 * Ingestion throughput (#1025) — how many matches the pipeline ingested per
 * period. Deliberately a separate call from `useMatchesOverTime`: the two answer
 * different questions and must never be mistaken for two views of one series.
 */
export function useMatchesIngested(
  granularity: MaybeRefOrGetter<IngestionTimeGranularity>,
  windowDays: MaybeRefOrGetter<number>,
) {
  return useOps<MatchesIngested>(
    '/stats/matches-ingested',
    () => ({ granularity: toValue(granularity), windowDays: toValue(windowDays) }),
  )
}

/** `GET /api/ops/db/tables` — table sizes/row estimates, sorted by total bytes. */
export function useDbTables() {
  return useOps<DbTableRow[]>('/db/tables')
}

/**
 * `GET /api/ops/db/history` — daily storage snapshots, per-table growth and the
 * disk forecast (#925). `windowDays` is reactive so the panel's window selector
 * refetches, matching the riot-usage panel's shape.
 */
export function useDbStorageHistory(windowDays: MaybeRefOrGetter<number>) {
  return useOps<DbStorageHistory>('/db/history', () => ({ windowDays: toValue(windowDays) }))
}

/**
 * `GET /api/ops/stats/aggregations` — per-family aggregate coverage (exact row
 * counts, champions/patches, freshness, latest run) plus the ingestion backlogs
 * that should read zero when the pipeline is caught up.
 */
export function useAggregations() {
  return useOps<AggregationsResponse>('/stats/aggregations')
}

/**
 * `GET /api/ops/patch-coverage` — whether the patches the public reads are
 * actually servable (#1033).
 *
 * A page-load `useFetch` rather than an on-demand `$fetch` because it *is* the
 * page: the whole route exists to answer one question, so there is nothing to
 * show before it resolves. It is still a set of grouped scans over tables with no
 * index on their patch column, which is why it lives on its own route instead of
 * as a card on `/aggregation`.
 */
export function usePatchCoverage() {
  return useOps<PatchCoverageResponse>('/patch-coverage')
}
