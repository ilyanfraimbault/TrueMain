import type { MaybeRefOrGetter } from 'vue'
import type {
  ChampionStatsFilters,
  ChampionStatsRow,
  DbTableRow,
  LogsFilters,
  LogsResponse,
  OverviewStats,
  ProcessRunsFilters,
  ProcessRunsResponse,
} from '~~/shared/types/ops'

/**
 * Strip `undefined`, `null`, and empty-string values from a query object so an
 * unset filter is omitted from the request entirely (the backend treats an
 * absent param as "no filter"). Numbers — including `0` — are preserved.
 */
function cleanQuery(
  filters: Record<string, string | number | undefined>,
): Record<string, string | number> {
  const out: Record<string, string | number> = {}
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
 * watches it and re-fetches when it changes. We render client-side
 * (`server: false`) — the dashboard is gated behind an operator session and the
 * data is operational, not SEO-relevant, so blocking SSR on it buys nothing and
 * keeps the proxy off the critical render path.
 */
export function useOps<T>(
  path: string,
  query?: MaybeRefOrGetter<Record<string, string | number | undefined>>,
) {
  return useFetch<T>(`/api/ops${path}`, {
    query: query
      ? computed(() => cleanQuery(toValue(query)))
      : undefined,
    server: false,
    // Distinct per path so concurrent panels don't share a cache entry.
    key: `ops:${path}`,
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

/** `GET /api/ops/db/tables` — table sizes/row estimates, sorted by total bytes. */
export function useDbTables() {
  return useOps<DbTableRow[]>('/db/tables')
}

/** `GET /api/ops/process-runs` — recent runs + per-process rollup. */
export function useProcessRuns(
  filters?: MaybeRefOrGetter<ProcessRunsFilters>,
) {
  return useOps<ProcessRunsResponse>(
    '/process-runs',
    filters ? () => ({ ...toValue(filters) }) : undefined,
  )
}

/**
 * `GET /api/ops/logs` — server-paginated application logs, newest first. `level`
 * is a minimum-severity threshold; `search` is a case-insensitive match on
 * message/exception. Pass a reactive getter so the table re-fetches when a
 * filter or the page changes.
 */
export function useLogs(
  filters?: MaybeRefOrGetter<LogsFilters>,
) {
  return useOps<LogsResponse>(
    '/logs',
    filters ? () => ({ ...toValue(filters) }) : undefined,
  )
}
