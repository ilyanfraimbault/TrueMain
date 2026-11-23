import type { MaybeRefOrGetter } from 'vue'

/**
 * `GET /api/ops/desktop/usage` (#1805) — the desktop app's downloads and usage
 * over the last `windowDays` UTC days.
 */
export function useDesktopUsage(windowDays: MaybeRefOrGetter<number>) {
  return useOps('/desktop/usage', () => ({ windowDays: toValue(windowDays) }))
}
