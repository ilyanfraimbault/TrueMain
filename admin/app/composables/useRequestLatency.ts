import type { MaybeRefOrGetter } from 'vue'
import type { RequestLatency, RequestLatencyWindow } from '~~/shared/types/ops'

/**
 * `GET /api/ops/request-latency` (#2307) — every ops route's server-side latency over the
 * Performance tab's window, slowest p95 first.
 */
export function useRequestLatency(window: MaybeRefOrGetter<RequestLatencyWindow>) {
  return useOps<RequestLatency>('/request-latency', () => ({ window: toValue(window) }))
}
