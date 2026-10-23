import type { MaybeRefOrGetter } from 'vue'
import type {
  PersonaConflictDetail,
  PersonaConflictResolveBody,
  PersonaConflictResolved,
  PersonaConflictsFilters,
  PersonaConflictsResponse,
} from '~~/shared/types/ops'

// The known-players conflict queue of the ops API (#2253), built on `useOps`.

/**
 * `GET /api/ops/personas/conflicts` — accounts two known players claim, most
 * recently seen first, paged, with the open/resolved counts of the whole table.
 */
export function usePersonaConflicts(filters: MaybeRefOrGetter<PersonaConflictsFilters>) {
  return useOps<PersonaConflictsResponse>('/personas/conflicts', () => ({ ...toValue(filters) }))
}

/** `GET /api/ops/personas/conflicts/{id}` — one conflict with both known players in full. */
export function getPersonaConflict(id: string) {
  return $fetch<PersonaConflictDetail>(`/api/ops/personas/conflicts/${encodeURIComponent(id)}`)
}

/**
 * `POST /api/ops/personas/conflicts/{id}/resolve` — settle a conflict. Throws a
 * `FetchError` the caller catches: 409 when it was resolved meanwhile or the
 * account changed hands, 400 on a malformed action.
 */
export function resolvePersonaConflict(id: string, body: PersonaConflictResolveBody) {
  return $fetch<PersonaConflictResolved>(`/api/ops/personas/conflicts/${encodeURIComponent(id)}/resolve`, {
    method: 'POST',
    body,
  })
}
