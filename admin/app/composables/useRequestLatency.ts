import type { MaybeRefOrGetter } from 'vue'
import type { RequestLatencyWindow } from '~~/shared/types/ops/request-latency'

/**
 * `GET /api/ops/request-latency` (#2307) — every ops route's server-side latency over the
 * Performance tab's window, slowest p95 first.
 */
export function useRequestLatency(window: MaybeRefOrGetter<RequestLatencyWindow>) {
  return useOps('/request-latency', () => ({ window: toValue(window) }))
}
