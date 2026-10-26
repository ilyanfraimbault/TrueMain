import type { NavSummary } from '~~/shared/types/ops'

/** How often the sidebar re-asks while the tab is visible. */
export const NAV_SUMMARY_REFRESH_MS = 60_000

/**
 * `GET /api/ops/nav-summary` (#2294) — the sidebar's pending counts and the
 * cockpit's verdict, re-asked every minute while the tab is visible and paused
 * in a background tab (`useLiveRefresh`, #1411).
 *
 * `summary` is the last response only while the last request succeeded: a
 * failed fetch reads `null`, so the sidebar shows nothing rather than the
 * payload `useOps` keeps from the cache (#2306) or a count of zero. Mounted once,
 * by the layout; anything else wanting the current verdict reads `healthStatus`.
 */
export function useNavSummary() {
  const fetched = useOps<NavSummary>('/nav-summary')
  useLiveRefresh(fetched, { every: NAV_SUMMARY_REFRESH_MS })

  const summary = computed<NavSummary | null>(() =>
    fetched.error.value ? null : (fetched.data.value ?? null),
  )
  const healthStatus = computed(() => summary.value?.healthStatus ?? null)
  return { summary, healthStatus }
}
