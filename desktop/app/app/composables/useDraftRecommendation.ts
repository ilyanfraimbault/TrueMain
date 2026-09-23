import type { DraftRecommendation } from '~/types/draft'
import type { DraftState } from '~/types/lcu'

/**
 * The draft answer for the current champion select.
 *
 * Re-requested whenever the draft or the user's lane corrections change. The
 * previous enemy placement is sent back so an equally-good re-solve keeps the
 * panel where it is: picks land one at a time, and a panel that reshuffles
 * while it is being read — with a timer running — is worse than one that is
 * slightly wrong.
 */
export function useDraftRecommendation(
  draft: Ref<DraftState | null>,
  pinnedLanes: Ref<Record<number, string>>,
) {
  const recommendation = ref<DraftRecommendation | null>(null)
  const pending = ref(false)
  const error = ref<string | null>(null)

  /** Guards against an older answer landing after a newer one. */
  let latestRequest = 0

  async function fetchRecommendation() {
    const state = draft.value
    if (!state || !state.myPosition) {
      recommendation.value = null
      return
    }
    if (!insideTauri()) {
      // No Rust to ask. A dev scenario may carry the answer, so the lane panel
      // can be worked on in a browser; otherwise there is none.
      if (import.meta.dev) recommendation.value = useDevScenarios().recommendation.value
      return
    }

    const requestId = ++latestRequest
    pending.value = true
    error.value = null

    try {
      const { invoke } = await import('@tauri-apps/api/core')
      const answer = await invoke<DraftRecommendation>('draft_recommendation', {
        request: {
          position: state.myPosition,
          enemyChampions: state.enemyChampions,
          pinnedEnemyLanes: pinnedLanes.value,
          previousEnemyLanes: Object.fromEntries(
            (recommendation.value?.enemyLanes ?? []).map(l => [l.championId, l.position]),
          ),
          allies: {},
          bans: [...state.allyBans, ...state.enemyBans],
          candidates: [],
        },
      })

      // A stale answer must not overwrite a fresher one; champion select fires
      // these faster than they come back.
      if (requestId === latestRequest) recommendation.value = answer
    }
    catch (cause) {
      if (requestId === latestRequest) error.value = String(cause)
    }
    finally {
      if (requestId === latestRequest) pending.value = false
    }
  }

  watch(
    () => [draft.value?.enemyChampions, draft.value?.myPosition, pinnedLanes.value],
    fetchRecommendation,
    { deep: true, immediate: true },
  )

  return { recommendation, pending, error }
}
