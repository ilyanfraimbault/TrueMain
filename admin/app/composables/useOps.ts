import type { MaybeRefOrGetter } from 'vue'
import type {
  AccountExplorer,
  AggregateFreshnessResponse,
  AggregationsResponse,
  DataQualityDetectorsResponse,
  CandidateDetail,
  CandidateFunnel,
  CandidateQueueLatency,
  CandidatesFilters,
  CandidatesResponse,
  ChampionStatsFilters,
  ChampionStatsRow,
  CrashesFilters,
  CrashesResponse,
  DbStorageHistory,
  DbTableRow,
  EffectiveConfigurationOverviewResponse,
  IncompleteMatchesFilters,
  IncompleteMatchesResponse,
  IngestionTimeGranularity,
  LogsFilters,
  LogsResponse,
  MatchDataQualityDetail,
  MatchesIngested,
  MatchTimeBucket,
  MatchTimeGranularity,
  OverviewStats,
  ProcessIterationsFilters,
  ProcessIterationsResponse,
  ProcessRunsFilters,
  ProcessRunsResponse,
  RiotApiUsage,
  RiotUsageFilters,
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
 * watches it and re-fetches when it changes. We render client-side
 * (`server: false`) — the dashboard is gated behind an operator session and the
 * data is operational, not SEO-relevant, so blocking SSR on it buys nothing and
 * keeps the proxy off the critical render path.
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
 * `GET /api/ops/process-runs` — server-paginated runs (newest first) plus the
 * per-process rollup, which covers the full filtered set regardless of paging.
 * Pass a reactive getter so the table re-fetches when a filter or the page
 * changes.
 */
export function useProcessRuns(
  filters?: MaybeRefOrGetter<ProcessRunsFilters>,
) {
  return useOps<ProcessRunsResponse>(
    '/process-runs',
    filters ? () => ({ ...toValue(filters) }) : undefined,
  )
}

/**
 * `GET /api/ops/process-iterations` — recent pipeline iterations (newest first),
 * each carrying its ordered process runs. Feeds the chain view. Pass a reactive
 * getter so it re-fetches when the page changes.
 */
export function useProcessIterations(
  filters?: MaybeRefOrGetter<ProcessIterationsFilters>,
) {
  return useOps<ProcessIterationsResponse>(
    '/process-iterations',
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
 * `GET /api/ops/crashes` — server-paginated crash reports, newest first. Each row
 * carries the full report (exception chain, environment + memory snapshot, and the
 * recent log tail), so no separate detail request is needed. Pass a reactive getter
 * so the table re-fetches when a filter or the page changes.
 */
export function useCrashes(
  filters?: MaybeRefOrGetter<CrashesFilters>,
) {
  return useOps<CrashesResponse>(
    '/crashes',
    filters ? () => ({ ...toValue(filters) }) : undefined,
  )
}

/**
 * `GET /api/ops/riot-usage` — Riot API usage metrics over a relative window
 * (`1h`/`24h`/`7d`): totals, per-endpoint breakdown, status-code histogram,
 * call-volume time-series and the latest rate-limit snapshot. Pass a reactive
 * getter so the panel re-fetches when the window or endpoint filter changes.
 */
export function useRiotUsage(
  filters?: MaybeRefOrGetter<RiotUsageFilters>,
) {
  return useOps<RiotApiUsage>(
    '/riot-usage',
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
 * `GET /api/ops/data-quality/detectors` — the automated anomaly detectors (#924):
 * one card per detector with its verdict, headline number, drill-down rows and
 * the thresholds it judged against. Takes no filters; every threshold is
 * server-side configuration, not a query parameter.
 */
export function useDataQualityDetectors() {
  return useOps<DataQualityDetectorsResponse>('/data-quality/detectors')
}

/**
 * `GET /api/ops/data-quality/aggregate-freshness` — the per-champion freshness
 * breakdown. A one-shot `$fetch` on purpose: it is the one measurement needing a
 * grouped scan, so it runs on an explicit click rather than on page load.
 */
export function getAggregateFreshness() {
  return $fetch<AggregateFreshnessResponse>('/api/ops/data-quality/aggregate-freshness')
}

/**
 * `GET /api/ops/configuration` — what every host is actually running with
 * (#1034): the Api's own options, read live, plus the Ingestor's, published to
 * Mongo at its own boot. Takes no filters.
 */
export function useEffectiveConfiguration() {
  return useOps<EffectiveConfigurationOverviewResponse>('/configuration')
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
 * Candidate funnel throughput (#1024) — intake, promotion and outcome per period.
 * The historical half of the `/candidates` page: the list above it shows the
 * instantaneous status counts, which cannot tell a flowing funnel from a stalled one.
 */
export function useCandidateFunnel(
  granularity: MaybeRefOrGetter<IngestionTimeGranularity>,
  windowDays: MaybeRefOrGetter<number>,
) {
  return useOps<CandidateFunnel>(
    '/candidates/funnel',
    () => ({ granularity: toValue(granularity), windowDays: toValue(windowDays) }),
  )
}

/**
 * Queue-latency snapshot (#1024) — takes no window on purpose: it is computed from
 * the timestamps of the candidates retained right now, so there is no period to
 * select and it must never be presented as a historical average.
 */
export function useCandidateQueueLatency() {
  return useOps<CandidateQueueLatency>('/candidates/queue-latency')
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
 * `GET /api/ops/accounts/{nameTag}` — everything the pipeline knows about one
 * Riot ID (#1032). A one-shot `$fetch` because the explorer submits a search
 * imperatively rather than watching a reactive key.
 *
 * Unlike the other detail reads this one **never 404s**: an unknown Riot ID comes
 * back 200 in the `NeverDiscovered` state, so callers need no not-found branch.
 * It still throws a `FetchError` on 400 (malformed Riot ID, unknown region).
 */
export function getAccountExplorer(riotId: string, region?: string) {
  return $fetch<AccountExplorer>(
    `/api/ops/accounts/${encodeURIComponent(riotId)}`,
    { query: region ? { region } : undefined },
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
