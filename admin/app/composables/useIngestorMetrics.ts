import type { MaybeRefOrGetter } from 'vue'
import type { IngestorMetrics, RiotUsageWindow } from '~~/shared/types/ops'

/**
 * `GET /api/ops/ingestor-metrics` (#1636) — the Ingestor's own meter (run failures, Riot
 * rate-limit waits and 429s) over the Riot API tab's window.
 */
export function useIngestorMetrics(window: MaybeRefOrGetter<RiotUsageWindow>) {
  return useOps<IngestorMetrics>('/ingestor-metrics', () => ({ window: toValue(window) }))
}
