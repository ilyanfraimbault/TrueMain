import type { MaybeRefOrGetter } from 'vue'
import type {
  AccountExplorer,
  CandidateDetail,
  CandidateFunnel,
  CandidateQueueLatency,
  CandidatesFilters,
  CandidatesResponse,
  CandidateStock,
  IngestionTimeGranularity,
  SeedAccountBody,
  SeedAccountResponse,
  SeedRequestReadModel,
  SeedRequestsFilters,
  SeedRequestsResponse,
} from '~~/shared/types/ops'

// Account, seed and main-candidate reads (and the one seed write) of the ops API,
// built on `useOps` (`useOps.ts`).

/**
 * `GET /api/ops/accounts/seed` — the server-paginated seed-request queue, newest
 * first. Pass a reactive getter so the table re-fetches when a filter
 * (status/region/`search` over the Riot ID) or the page changes; call `refresh()`
 * after a submit to surface the new request.
 *
 * Paged rather than capped at a "recent" limit: the weekly OTP seeder adds tens of
 * thousands of requests at once, so a capped list showed a sliver of the queue and
 * no total (#1166).
 */
export function useSeedRequests(
  filters?: MaybeRefOrGetter<SeedRequestsFilters>,
) {
  return useOps<SeedRequestsResponse>(
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
 * Candidate stock (#1403) — the funnel's level per period, from the hourly snapshots.
 * The companion of `useCandidateFunnel`: that one says how much moved, this one says
 * how much is waiting. Both are needed, because a period that promotes everything it
 * scores leaves the level flat, and so does a pipeline that has stopped.
 */
export function useCandidateStock(
  granularity: MaybeRefOrGetter<IngestionTimeGranularity>,
  windowDays: MaybeRefOrGetter<number>,
) {
  return useOps<CandidateStock>(
    '/candidates/stock',
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
