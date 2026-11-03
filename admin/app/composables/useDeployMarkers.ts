import { computed, toValue } from 'vue'
import type { MaybeRefOrGetter } from 'vue'
import type { Deployments } from '~~/shared/types/ops'
import { placeDeployMarkers } from '~/utils/deploy-markers'

/**
 * How far back the deploy list reaches: the widest chart on the portal (database growth)
 * shows a year. One window for every chart, so they share a single request and its cache
 * entry instead of one per chart and per selected range.
 */
const DEPLOYMENTS_LOOKBACK_DAYS = 395

/** The window start, truncated to the UTC day so the request key is stable all day. */
function lookbackStart(): string {
  const today = new Date()
  today.setUTCHours(0, 0, 0, 0)
  return new Date(today.getTime() - DEPLOYMENTS_LOOKBACK_DAYS * 86_400_000).toISOString()
}

/** `GET /api/ops/deployments` (#2297) — every backend version change over the lookback. */
export function useDeployments() {
  return useOps<Deployments>('/deployments', { from: lookbackStart() })
}

/**
 * The deploy markers of a chart whose rows start at `bucketStarts` (ISO, ascending). Markers
 * are chrome, not data: a failed or pending deploy read draws none rather than an error.
 */
export function useDeployMarkers(bucketStarts: MaybeRefOrGetter<readonly string[]>) {
  const { data } = useDeployments()
  return computed(() => placeDeployMarkers(toValue(bucketStarts), data.value?.deployments ?? []))
}
