import type { MaybeRefOrGetter } from 'vue'
import type {
  CandidateDetail,
  CandidatesFilters,
  CandidatesResponse,
  ChampionStatsFilters,
  ChampionStatsRow,
  DbTableRow,
  IncompleteMatchesFilters,
  IncompleteMatchesResponse,
  LogsFilters,
  LogsResponse,
  MatchDataQualityDetail,
  MatchTimeBucket,
  MatchTimeGranularity,
  OverviewStats,
  ProcessRunsFilters,
  ProcessRunsResponse,
  SeedAccountBody,
  SeedAccountResponse,
  SeedRequestReadModel,
  SeedRequestsFilters,
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
  const queryParams = query
    ? computed(() => cleanQuery(toValue(query)))
    : undefined
  return useFetch<T>(`/api/ops${path}`, {
    query: queryParams,
    server: false,
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
 * the given granularity (week/month/year/patch), returned chronologically. Pass a
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

/**
 * `GET /api/ops/data-quality/incomplete-matches` — matches flagged by the
 * data-quality checks, grouped by issue type and queue-scoped. Pass a reactive
 * getter so the panel re-fetches when a filter or the page changes.
 */
export function useIncompleteMatches(
  filters?: MaybeRefOrGetter<IncompleteMatchesFilters>,
) {
  return useOps<IncompleteMatchesResponse>(
    '/data-quality/incomplete-matches',
    filters ? () => ({ ...toValue(filters) }) : undefined,
  )
}

/**
 * `GET /api/ops/data-quality/match/{id}` — per-match detail (both teams by
 * position with gaps highlighted). A one-shot `$fetch` because the slide-over
 * loads it imperatively on row click / deep-link rather than watching a key.
 *
 * Throws a `FetchError` on any non-2xx response (`$fetch` rejects rather than
 * returning null) — including 404 for an unknown match. Callers must wrap the
 * call in try/catch and inspect `statusCode === 404` to treat "no such match"
 * as an empty result, as `openDetail` in `pages/data-quality.vue` does.
 */
export function getMatchDataQuality(id: string) {
  return $fetch<MatchDataQualityDetail>(
    `/api/ops/data-quality/match/${encodeURIComponent(id)}`,
  )
}

/**
 * `GET /api/ops/accounts/seed` — recent seed requests, newest first. Pass a
 * reactive getter so the table re-fetches when the status filter or `search`
 * (Riot ID gameName/tagLine substring) changes; call `refresh()` after a submit
 * to surface the new request.
 */
export function useSeedRequests(
  filters?: MaybeRefOrGetter<SeedRequestsFilters>,
) {
  return useOps<SeedRequestReadModel[]>(
    '/accounts/seed',
    filters ? () => ({ ...toValue(filters) }) : undefined,
  )
}

/**
 * `GET /api/ops/candidates` — the server-paginated main-candidate ingestion
 * pipeline list, most-relevant first. Pass a reactive getter so the table
 * re-fetches when a filter (status/region/search) or the page changes.
 */
export function useCandidates(
  filters?: MaybeRefOrGetter<CandidatesFilters>,
) {
  return useOps<CandidatesResponse>(
    '/candidates',
    filters ? () => ({ ...toValue(filters) }) : undefined,
  )
}

/**
 * `GET /api/ops/candidates/{id}` — one candidate's detail (pipeline fields,
 * ingested match count, linked seed request). A one-shot `$fetch` because the
 * slide-over loads it imperatively on row click / deep-link rather than watching
 * a key. Throws a `FetchError` on any non-2xx (including 404 for an unknown id);
 * callers must catch and inspect `statusCode === 404`.
 */
export function getCandidateDetail(id: string) {
  return $fetch<CandidateDetail>(
    `/api/ops/candidates/${encodeURIComponent(id)}`,
  )
}

/**
 * `GET /api/ops/accounts/seed/{id}` — a single seed request's current state.
 * A one-shot `$fetch` (not `useFetch`) because callers poll it imperatively on
 * a timer until the status is terminal, rather than reactively watching a key.
 */
export function getSeedRequest(id: string) {
  return $fetch<SeedRequestReadModel>(`/api/ops/accounts/seed/${encodeURIComponent(id)}`)
}

/**
 * `POST /api/ops/accounts/seed` — queue a Riot ID for ingestion. A mutation, so
 * it uses `$fetch` rather than `useFetch`. Idempotent on the backend: re-posting
 * the same (gameName, tagLine, platformId) returns the existing pending request.
 * Throws an `FetchError` (e.g. 400 on bad input) the caller is expected to catch.
 */
export function seedAccount(body: SeedAccountBody) {
  return $fetch<SeedAccountResponse>('/api/ops/accounts/seed', {
    method: 'POST',
    body,
  })
}
