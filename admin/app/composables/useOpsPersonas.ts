import type { MaybeRefOrGetter } from 'vue'
import type {
  PersonaAccountInput,
  PersonaAdminDetail,
  PersonaAutoMerged,
  PersonaChannelConflictsFilters,
  PersonaChannelConflictsResponse,
  PersonaConflictDetail,
  PersonaConflictResolveBody,
  PersonaConflictResolved,
  PersonaConflictsFilters,
  PersonaConflictsResponse,
  PersonaCreateBody,
  PersonaEdited,
  PersonaHomonymDistinctBody,
  PersonaHomonymMarked,
  PersonaHomonymMergeBody,
  PersonaHomonymMerged,
  PersonaHomonymsFilters,
  PersonaHomonymsResponse,
  PersonaLinkInput,
  PersonaListFilters,
  PersonasPage,
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

/**
 * `POST /api/ops/personas/conflicts/auto-merge` — settle as merges the open
 * conflicts whose two known players share a name (#2271). The intake runs the
 * same sweep after every batch; this one clears the backlog.
 */
export function autoMergePersonaConflicts() {
  return $fetch<PersonaAutoMerged>('/api/ops/personas/conflicts/auto-merge', { method: 'POST' })
}

/**
 * `GET /api/ops/personas/channel-conflicts` — known players listing the same social
 * channel (#2286), the most channels in common first, paged. Settled with the
 * homonyms actions below: a merge, or a mark as different people.
 */
export function usePersonaChannelConflicts(filters: MaybeRefOrGetter<PersonaChannelConflictsFilters>) {
  return useOps<PersonaChannelConflictsResponse>('/personas/channel-conflicts', () => ({ ...toValue(filters) }))
}

// Known players sharing a name without a common account (#2271): the operator
// merges them or marks them as different people. Both throw a `FetchError`: 400
// on a malformed body, 409 when one was merged or removed meanwhile.

/** `GET /api/ops/personas/homonyms` — groups of same-name known players, in name order, paged. */
export function usePersonaHomonyms(filters: MaybeRefOrGetter<PersonaHomonymsFilters>) {
  return useOps<PersonaHomonymsResponse>('/personas/homonyms', () => ({ ...toValue(filters) }))
}

/** `POST /api/ops/personas/homonyms/merge` — fold known players into the one kept. */
export function mergePersonaHomonyms(body: PersonaHomonymMergeBody) {
  return $fetch<PersonaHomonymMerged>('/api/ops/personas/homonyms/merge', { method: 'POST', body })
}

/** `POST /api/ops/personas/homonyms/distinct` — mark known players as different people. */
export function markPersonaHomonymsDistinct(body: PersonaHomonymDistinctBody) {
  return $fetch<PersonaHomonymMarked>('/api/ops/personas/homonyms/distinct', { method: 'POST', body })
}

// The known-players management of the ops API (#2019). Every edit throws a
// `FetchError` the caller catches: 400 on a malformed body, 404 for an unknown
// player or row, 409 when the slug or link exists or the player was merged or
// removed meanwhile.

const personaPath = (id: string) => `/api/ops/personas/${encodeURIComponent(id)}`

/** `GET /api/ops/personas` — known players, most recently updated first, paged. */
export function usePersonas(filters: MaybeRefOrGetter<PersonaListFilters>) {
  return useOps<PersonasPage>('/personas', () => ({ ...toValue(filters) }))
}

/**
 * `GET /api/ops/personas?search=` as a one-shot read, for the ⌘K palette (#2290):
 * it fires per debounced keystroke and its answer only lives while the palette is
 * open, so it stays out of `useOps` and its cache.
 */
export function searchPersonas(search: string, pageSize: number) {
  return $fetch<PersonasPage>('/api/ops/personas', { query: { search, pageSize } })
}

/** `GET /api/ops/personas/{id}` — one known player in full. */
export function getPersona(id: string) {
  return $fetch<PersonaAdminDetail>(personaPath(id))
}

/** `POST /api/ops/personas` — add a known player by hand (source `manual`). */
export function createPersona(body: PersonaCreateBody) {
  return $fetch<PersonaEdited>('/api/ops/personas', { method: 'POST', body })
}

/** `PATCH /api/ops/personas/{id}` — the operator's name, which the sources then keep. */
export function renamePersona(id: string, displayName: string) {
  return $fetch<PersonaEdited>(personaPath(id), { method: 'PATCH', body: { displayName } })
}

/** `DELETE /api/ops/personas/{id}` — remove a known player; `takedown` also bars its accounts everywhere. */
export function removePersona(id: string, takedown: boolean) {
  return $fetch<PersonaEdited>(personaPath(id), { method: 'DELETE', query: takedown ? { takedown: true } : undefined })
}

export function addPersonaLink(id: string, link: PersonaLinkInput) {
  return $fetch<PersonaEdited>(`${personaPath(id)}/links`, { method: 'POST', body: link })
}

export function removePersonaLink(id: string, linkId: string) {
  return $fetch<PersonaEdited>(`${personaPath(id)}/links/${encodeURIComponent(linkId)}`, { method: 'DELETE' })
}

export function addPersonaAccount(id: string, account: PersonaAccountInput) {
  return $fetch<PersonaEdited>(`${personaPath(id)}/accounts`, { method: 'POST', body: account })
}

export function unlinkPersonaAccount(id: string, accountId: string) {
  return $fetch<PersonaEdited>(`${personaPath(id)}/accounts/${encodeURIComponent(accountId)}`, { method: 'DELETE' })
}
