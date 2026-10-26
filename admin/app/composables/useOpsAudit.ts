import type { MaybeRefOrGetter } from 'vue'
import type { AuditEventsFilters, AuditEventsResponse } from '~~/shared/types/ops'

// The operator-action trail of the ops API (#2288), built on `useOps`.

/** `GET /api/ops/audit-events` — the Audit page: every operator action, newest first, paged and filtered. */
export function useAuditEvents(filters: MaybeRefOrGetter<AuditEventsFilters>) {
  return useOps<AuditEventsResponse>('/audit-events', () => ({ ...toValue(filters) }))
}

/** The same read on demand, for one target's history (the known-player panel). */
export function getAuditEvents(query: AuditEventsFilters) {
  return $fetch<AuditEventsResponse>('/api/ops/audit-events', { query })
}
