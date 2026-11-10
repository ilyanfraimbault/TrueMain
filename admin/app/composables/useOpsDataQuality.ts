import type { MaybeRefOrGetter } from 'vue'
import type { OpsQueryOf } from './useOps'

// Data-quality reads of the ops API, built on `useOps` (`useOps.ts`).

/**
 * `GET /api/ops/data-quality/incomplete-matches` — matches flagged by the
 * data-quality checks, grouped by issue type and queue-scoped. Pass a reactive
 * getter so the panel re-fetches when a filter or the page changes.
 */
export function useIncompleteMatches(filters?: MaybeRefOrGetter<OpsQueryOf<'/data-quality/incomplete-matches'>>) {
  return useOps('/data-quality/incomplete-matches', filters)
}

/**
 * `GET /api/ops/data-quality/detectors` — the automated anomaly detectors (#924):
 * one card per detector with its verdict, headline number, drill-down rows and
 * the thresholds it judged against. Takes no filters; every threshold is
 * server-side configuration, not a query parameter.
 */
export function useDataQualityDetectors() {
  return useOps('/data-quality/detectors')
}

/**
 * `GET /api/ops/data-quality/aggregate-freshness` — the per-champion freshness
 * breakdown. A one-shot `opsCall` on purpose: it is the one measurement needing a
 * grouped scan, so it runs on an explicit click rather than on page load.
 */
export function getAggregateFreshness() {
  return opsCall('get', '/data-quality/aggregate-freshness')
}

/**
 * `GET /api/ops/data-quality/match/{id}` — per-match detail (both teams by
 * position with gaps highlighted). A one-shot `opsCall` because the slide-over
 * loads it imperatively on row click / deep-link rather than watching a key.
 *
 * Throws a `FetchError` on any non-2xx response (it rejects rather than
 * returning null) — including 404 for an unknown match. Callers must wrap the
 * call in try/catch and inspect `statusCode === 404` to treat "no such match"
 * as an empty result, as `useDeepLinkedDetail` does for `pages/data-quality.vue`.
 */
export function getMatchDataQuality(id: string) {
  return opsCall('get', '/data-quality/match/{id}', { path: { id } })
}
