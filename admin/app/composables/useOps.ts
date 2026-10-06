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
 * Strip `undefined`, `null`, and empty-string values from a query object so an
 * unset filter is omitted from the request entirely (the backend treats an
 * absent param as "no filter"). Numbers — including `0` — are preserved.
 */
function cleanQuery(
  filters: Record<string, string | number | boolean | undefined>,
): Record<string, string | number | boolean> {
  const out: Record<string, string | number | boolean> = {}
  for (const [key, value] of Object.entries(filters)) {
    if (value === undefined || value === null || value === '') {
      continue
    }
    out[key] = value
  }
  return out
}

/**
 * Thin wrapper around `useFetch('/api/ops' + path)` — the browser-facing,
 * session-authenticated proxy to the backend ops API. Returns the standard
 * `useFetch` shape (`data`, `pending`, `error`, `refresh`, `status`).
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
  return useFetch<T>(`/api/ops${path}`, {
    query: queryParams,
    // Distinct per (path, query) so concurrent panels hitting the same path with
    // different filters don't collide on one cache entry. Without the query in
    // the key, e.g. the Overview's unfiltered `/stats/champions` and the
    // Champions panel's region-filtered one would share `ops:/stats/champions`
    // and clobber each other's data.
    key: queryParams
      ? computed(() => `ops:${path}:${JSON.stringify(queryParams.value)}`)
      : `ops:${path}`,
  })
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
