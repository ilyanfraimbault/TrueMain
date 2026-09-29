import type { DraftCandidate, DraftRecommendation } from '~/types/draft'
import type { DraftState } from '~/types/lcu'

/** Picks and corrections landing together collapse into one request. */
const DEBOUNCE_MS = 120

/** What the endpoint scores in one request (`MaxCandidates` on the API). */
const CANDIDATES_PER_REQUEST = 40

/**
 * The endpoint's own order, restated for answers merged from several requests:
 * a proven pick before an unproven one, then the score, then the id. Each
 * candidate's score depends on it alone, so batches merge without distortion.
 */
function byServerOrder(a: DraftCandidate, b: DraftCandidate) {
  return Number(a.thinSample) - Number(b.thinSample) || b.score - a.score || a.championId - b.championId
}

/**
 * The draft answer for the current champion select: the enemy lanes, our lane
 * opponent, and the candidates ranked against both.
 *
 * Re-requested whenever the draft, the user's lane corrections or the pool
 * change. The previous enemy placement is sent back so an equally-good
 * re-solve keeps the panel where it is: picks land one at a time, and a panel
 * that reshuffles while it is being read — with a timer running — is worse
 * than one that is slightly wrong.
 */
export function useDraftRecommendation(
  draft: Ref<DraftState | null>,
  pinnedLanes: Ref<Record<number, string>>,
  candidates: Ref<number[]>,
) {
  const recommendation = ref<DraftRecommendation | null>(null)
  const pending = ref(false)
  const error = ref<string | null>(null)

  /** Allies locked in, keyed by the lane the client assigned them — the synergy half's input. */
  const allies = computed(() => Object.fromEntries((draft.value?.myTeam ?? [])
    .filter(slot => slot.locked && !slot.isMe && slot.championId !== null && slot.position)
    .map(slot => [slot.position, slot.championId])))

  /** Guards against an older answer landing after a newer one. */
  let latestRequest = 0
  let timer: ReturnType<typeof setTimeout> | undefined

  async function ask(request: object): Promise<DraftRecommendation> {
    if (insideTauri()) {
      const { invoke } = await import('@tauri-apps/api/core')
      return await invoke<DraftRecommendation>('draft_recommendation', { request })
    }
    // `npm run dev` in a browser: the dev server proxies `/api` (nuxt.config).
    return await $fetch<DraftRecommendation>('/api/champions/draft', { method: 'POST', body: request })
  }

  async function fetchRecommendation() {
    const state = draft.value
    if (!state || !state.myPosition) {
      recommendation.value = null
      return
    }

    const requestId = ++latestRequest
    pending.value = true
    error.value = null

    const base = {
      position: state.myPosition,
      enemyChampions: state.enemyChampions,
      pinnedEnemyLanes: pinnedLanes.value,
      previousEnemyLanes: Object.fromEntries(
        (recommendation.value?.enemyLanes ?? []).map(lane => [lane.championId, lane.position]),
      ),
      allies: allies.value,
      bans: [...state.allyBans, ...state.enemyBans],
    }
    // A pool wider than one request is split, and the answers merged: the
    // lanes come from the first, the candidates from all of them.
    const batches: number[][] = []
    for (let start = 0; start < candidates.value.length; start += CANDIDATES_PER_REQUEST) {
      batches.push(candidates.value.slice(start, start + CANDIDATES_PER_REQUEST))
    }
    if (!batches.length) batches.push([])

    try {
      const answers = await Promise.all(batches.map(batch => ask({ ...base, candidates: batch })))
      const [first] = answers
      // A stale answer must not overwrite a fresher one; champion select fires
      // these faster than they come back.
      if (first && requestId === latestRequest) {
        recommendation.value = { ...first, candidates: answers.flatMap(answer => answer.candidates).sort(byServerOrder) }
      }
    }
    catch (cause) {
      if (requestId !== latestRequest) return
      error.value = String(cause)
      // Offline in `npm run dev`: a scenario may carry the answer the API gave.
      if (import.meta.dev && !insideTauri()) recommendation.value = useDevScenarios().recommendation.value
    }
    finally {
      if (requestId === latestRequest) pending.value = false
    }
  }

  // Watched as a string: the draft object is replaced on every client push,
  // the timer ticking included, and only a change in what is asked is a reason
  // to ask again.
  const key = computed(() => JSON.stringify([
    draft.value?.enemyChampions,
    draft.value?.myPosition,
    draft.value?.allyBans,
    draft.value?.enemyBans,
    pinnedLanes.value,
    allies.value,
    candidates.value,
  ]))

  watch(key, () => {
    clearTimeout(timer)
    timer = setTimeout(fetchRecommendation, DEBOUNCE_MS)
  }, { immediate: true })

  onScopeDispose(() => clearTimeout(timer))

  return { recommendation, pending, error }
}
