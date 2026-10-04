import type { MaybeRefOrGetter } from 'vue'
import type { RiotQuota, RiotUsageWindow } from '~~/shared/types/ops'

/**
 * `GET /api/ops/riot-quota` (#1458) — quota utilisation per routing host and lane duty
 * cycle over the Riot API tab's window. Out of `useOps.ts`, which is past the size limit
 * and may only shrink.
 */
export function useRiotQuota(window: MaybeRefOrGetter<RiotUsageWindow>) {
  return useOps<RiotQuota>('/riot-quota', () => ({ window: toValue(window) }))
}
