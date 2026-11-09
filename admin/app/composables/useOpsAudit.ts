import type { MaybeRefOrGetter } from 'vue'
import type { OpsQueryOf } from './useOps'

// The operator-action trail of the ops API (#2288), built on `useOps`.

/** `GET /api/ops/audit-events` — the Audit page: every operator action, newest first, paged and filtered. */
export function useAuditEvents(filters: MaybeRefOrGetter<OpsQueryOf<'/audit-events'>>) {
  return useOps('/audit-events', filters)
}

/** The same read on demand, for one target's history (the known-player panel). */
export function getAuditEvents(query: OpsQueryOf<'/audit-events'>) {
  return opsCall('get', '/audit-events', { query })
}
