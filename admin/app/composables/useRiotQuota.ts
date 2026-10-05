import type { MaybeRefOrGetter } from 'vue'
import type { RiotQuota, RiotUsageWindow } from '~~/shared/types/ops'

/**
 * `GET /api/ops/riot-quota` (#1458) — quota utilisation per routing host and lane duty
 * cycle over the Riot API tab's window.
 */
export function useRiotQuota(window: MaybeRefOrGetter<RiotUsageWindow>) {
  return useOps<RiotQuota>('/riot-quota', () => ({ window: toValue(window) }))
}
