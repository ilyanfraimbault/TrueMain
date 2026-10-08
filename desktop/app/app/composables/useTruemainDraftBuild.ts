import type { CompositionBuildRequest, CompositionBuildResponse } from '~/types/build'
import type { DraftBuild } from '~/composables/useDraftBuild'
import { draftBuildOf } from '~/composables/useDraftBuild'

/** A main's draft build: `null` when they have no game of this draft to read it from. */
type Answer = DraftBuild | null

/**
 * One true main's own build against the draft as it stands (#1987): the draft's
 * composition build, read from their games alone (`player` on the request). A
 * main who never faced the lane opponent, or never played the champion on the
 * lane, answers `null` — the view then shows their usual build.
 *
 * Asked through the shell's shared POST, not `composition_build`: that command
 * keeps a single request in flight and would cancel the draft's own build.
 * Answers are kept for the session, per main and per draft; a failed ask is not,
 * so the next click asks again.
 */
export function useTruemainDraftBuild() {
  const cache = useState<Record<string, Answer>>('truemain-draft-builds', () => ({}))
  const pending = useState<Record<string, boolean>>('truemain-draft-builds-pending', () => ({}))
  const failed = useState<Record<string, boolean>>('truemain-draft-builds-failed', () => ({}))

  const keyOf = (nameTag: string, championId: number, request: CompositionBuildRequest) =>
    `${nameTag}:${championId}:${JSON.stringify(request)}`

  async function load(nameTag: string, championId: number, request: CompositionBuildRequest) {
    const key = keyOf(nameTag, championId, request)
    if (key in cache.value || pending.value[key]) return
    pending.value = { ...pending.value, [key]: true }
    failed.value = { ...failed.value, [key]: false }
    try {
      const answer = await apiPost<CompositionBuildResponse>(`/champions/${championId}/composition-build`, { ...request, player: nameTag })
      const found = !(answer.matchupRequested && !answer.matchupFound) && answer.build.gamesConsidered > 0
      cache.value = { ...cache.value, [key]: found ? draftBuildOf(championId, answer) : null }
    }
    catch {
      failed.value = { ...failed.value, [key]: true }
    }
    finally {
      pending.value = { ...pending.value, [key]: false }
    }
  }

  /** Their draft build, `null` without one, `undefined` while it has not come back. */
  const answerOf = (nameTag: string, championId: number, request: CompositionBuildRequest) => cache.value[keyOf(nameTag, championId, request)]
  const hasFailed = (nameTag: string, championId: number, request: CompositionBuildRequest) => !!failed.value[keyOf(nameTag, championId, request)]

  return { load, answerOf, hasFailed }
}
