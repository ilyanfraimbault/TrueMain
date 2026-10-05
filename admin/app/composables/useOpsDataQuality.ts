import type { MaybeRefOrGetter } from 'vue'
import type {
  AggregateFreshnessResponse,
  DataQualityDetectorsResponse,
  IncompleteMatchesFilters,
  IncompleteMatchesResponse,
  MatchDataQualityDetail,
} from '~~/shared/types/ops'

// Data-quality reads of the ops API, built on `useOps` (`useOps.ts`).

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
 * `GET /api/ops/data-quality/match/{id}` — per-match detail (both teams by
 * position with gaps highlighted). A one-shot `$fetch` because the slide-over
 * loads it imperatively on row click / deep-link rather than watching a key.
 *
 * Throws a `FetchError` on any non-2xx response (`$fetch` rejects rather than
 * returning null) — including 404 for an unknown match. Callers must wrap the
 * call in try/catch and inspect `statusCode === 404` to treat "no such match"
 * as an empty result, as `useDeepLinkedDetail` does for `pages/data-quality.vue`.
 */
export function getMatchDataQuality(id: string) {
  return $fetch<MatchDataQualityDetail>(
    `/api/ops/data-quality/match/${encodeURIComponent(id)}`,
  )
}
