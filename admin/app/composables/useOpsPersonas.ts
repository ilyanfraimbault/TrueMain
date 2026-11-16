import type { MaybeRefOrGetter } from 'vue'
import type { PersonaAccountEntry, PersonaConflictBulkResolveRequest, PersonaConflictResolveRequest, PersonaCreateRequest, PersonaHomonymDistinctRequest, PersonaHomonymMergeRequest, PersonaLinkEntry } from '#site-shared/contract/api'
import type { OpsQueryOf } from './useOps'

// The known-players conflict queue of the ops API (#2253), built on `useOps`.

/**
 * `GET /api/ops/personas/conflicts` — accounts two known players claim, most
 * recently seen first, paged, with the open/resolved counts of the whole table.
 */
export function usePersonaConflicts(filters: MaybeRefOrGetter<OpsQueryOf<'/personas/conflicts'>>) {
  return useOps('/personas/conflicts', filters)
}

/** `GET /api/ops/personas/conflicts/{id}` — one conflict with both known players in full. */
export function getPersonaConflict(id: string) {
  return opsCall('get', '/personas/conflicts/{id}', { path: { id } })
}

/**
 * `POST /api/ops/personas/conflicts/{id}/resolve` — settle a conflict. Throws a
 * `FetchError` the caller catches: 409 when it was resolved meanwhile or the
 * account changed hands, 400 on a malformed action.
 */
export function resolvePersonaConflict(id: string, body: PersonaConflictResolveRequest) {
  return opsCall('post', '/personas/conflicts/{id}/resolve', { path: { id }, body })
}

/**
 * `GET /api/ops/personas/conflicts/bulk-resolve/preview` — the open conflicts a bulk
 * merge at `minScore` would settle (#2295), each with the known player it keeps.
 */
export function previewBulkResolve(minScore: number) {
  return opsCall('get', '/personas/conflicts/bulk-resolve/preview', { query: { minScore } })
}

/**
 * `POST /api/ops/personas/conflicts/bulk-resolve` — merge exactly the previewed
 * conflicts; the API re-checks each and answers one outcome per id.
 */
export function bulkResolvePersonaConflicts(body: PersonaConflictBulkResolveRequest) {
  return opsCall('post', '/personas/conflicts/bulk-resolve', { body })
}

/**
 * `POST /api/ops/personas/conflicts/auto-merge` — settle as merges the open
 * conflicts whose two known players share a name (#2271). The intake runs the
 * same sweep after every batch; this one clears the backlog.
 */
export function autoMergePersonaConflicts() {
  return opsCall('post', '/personas/conflicts/auto-merge')
}

/**
 * `GET /api/ops/personas/channel-conflicts` — known players listing the same social
 * channel (#2286), the most channels in common first, paged. Settled with the
 * homonyms actions below: a merge, or a mark as different people.
 */
export function usePersonaChannelConflicts(filters: MaybeRefOrGetter<OpsQueryOf<'/personas/channel-conflicts'>>) {
  return useOps('/personas/channel-conflicts', filters)
}

// Known players sharing a name without a common account (#2271): the operator
// merges them or marks them as different people. Both throw a `FetchError`: 400
// on a malformed body, 409 when one was merged or removed meanwhile.

/** `GET /api/ops/personas/homonyms` — groups of same-name known players, in name order, paged. */
export function usePersonaHomonyms(filters: MaybeRefOrGetter<OpsQueryOf<'/personas/homonyms'>>) {
  return useOps('/personas/homonyms', filters)
}

/** `POST /api/ops/personas/homonyms/merge` — fold known players into the one kept. */
export function mergePersonaHomonyms(body: PersonaHomonymMergeRequest) {
  return opsCall('post', '/personas/homonyms/merge', { body })
}

/** `POST /api/ops/personas/homonyms/distinct` — mark known players as different people. */
export function markPersonaHomonymsDistinct(body: PersonaHomonymDistinctRequest) {
  return opsCall('post', '/personas/homonyms/distinct', { body })
}

// The known-players management of the ops API (#2019). Every edit throws a
// `FetchError` the caller catches: 400 on a malformed body, 404 for an unknown
// player or row, 409 when the slug or link exists or the player was merged or
// removed meanwhile.

/** `GET /api/ops/personas` — known players, most recently updated first, paged. */
export function usePersonas(filters: MaybeRefOrGetter<OpsQueryOf<'/personas'>>) {
  return useOps('/personas', filters)
}

/**
 * `GET /api/ops/personas?search=` as a one-shot read, for the ⌘K palette (#2290):
 * it fires per debounced keystroke and its answer only lives while the palette is
 * open, so it stays out of `useOps` and its cache.
 */
export function searchPersonas(search: string, pageSize: number) {
  return opsCall('get', '/personas', { query: { search, pageSize } })
}

/** `GET /api/ops/personas/{id}` — one known player in full. */
export function getPersona(id: string) {
  return opsCall('get', '/personas/{id}', { path: { id } })
}

/** `POST /api/ops/personas` — add a known player by hand (source `manual`). */
export function createPersona(body: PersonaCreateRequest) {
  return opsCall('post', '/personas', { body })
}

/** `PATCH /api/ops/personas/{id}` — the operator's name, which the sources then keep. */
export function renamePersona(id: string, displayName: string) {
  return opsCall('patch', '/personas/{id}', { path: { id }, body: { displayName } })
}

/** `DELETE /api/ops/personas/{id}` — remove a known player; `takedown` also bars its accounts everywhere. */
export function removePersona(id: string, takedown: boolean) {
  return opsCall('delete', '/personas/{id}', { path: { id }, query: takedown ? { takedown: true } : undefined })
}

export function addPersonaLink(id: string, link: PersonaLinkEntry) {
  return opsCall('post', '/personas/{id}/links', { path: { id }, body: link })
}

export function removePersonaLink(id: string, linkId: string) {
  return opsCall('delete', '/personas/{id}/links/{linkId}', { path: { id, linkId } })
}

export function addPersonaAccount(id: string, account: PersonaAccountEntry) {
  return opsCall('post', '/personas/{id}/accounts', { path: { id }, body: account })
}

export function unlinkPersonaAccount(id: string, accountId: string) {
  return opsCall('delete', '/personas/{id}/accounts/{accountId}', { path: { id, accountId } })
}
