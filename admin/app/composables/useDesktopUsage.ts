import type { MaybeRefOrGetter } from 'vue'
import type { DesktopUsage } from '~~/shared/types/desktop'

/**
 * `GET /api/ops/desktop/usage` (#1805) — the desktop app's downloads and usage
 * over the last `windowDays` UTC days. Out of `useOps.ts`, which is past the size
 * limit and may only shrink.
 */
export function useDesktopUsage(windowDays: MaybeRefOrGetter<number>) {
  return useOps<DesktopUsage>('/desktop/usage', () => ({ windowDays: toValue(windowDays) }))
}
