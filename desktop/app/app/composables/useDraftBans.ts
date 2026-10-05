import type { DraftBans } from '~/types/draft'
import type { DraftState } from '~/types/lcu'

/** Bans and hovers landing together collapse into one request. */
const DEBOUNCE_MS = 120

/** Pool champions protected when no pick is declared (`MaxPoolTargets` on the API). */
const POOL_TARGETS = 10

/**
 * The ban suggestions for the current champion select (#1906): the champions
 * that threaten the pick the player declared — their own hover, in the planning
 * phase or later — or, without one, their pool.
 *
 * Only asked while `active`: bans are a question for our ban turn, and the
 * request is not worth sending on every pick of the draft. The pool is the
 * client's mastery order; its weight is that rank (the client hands the app the
 * order, not the points), so the most-played champion counts most.
 */
export function useDraftBans(
  draft: Ref<DraftState | null>,
  pool: Ref<number[]>,
  active: Ref<boolean>,
) {
  const bans = ref<DraftBans | null>(null)
  const pending = ref(false)
  const error = ref<string | null>(null)

  let latestRequest = 0
  let timer: ReturnType<typeof setTimeout> | undefined

  async function ask(request: object): Promise<DraftBans> {
    if (insideTauri()) {
      const { invoke } = await import('@tauri-apps/api/core')
      return await invoke<DraftBans>('draft_bans', { request })
    }
    // `npm run dev` in a browser: the dev server proxies `/api` (nuxt.config).
    return await $fetch<DraftBans>('/api/champions/draft/bans', { method: 'POST', body: request })
  }

  /** Our hover, not yet locked: the pick the bans protect. */
  const plannedPick = computed(() => {
    const state = draft.value
    return state && !state.myChampionLocked ? state.myChampion : null
  })

  const request = computed(() => {
    const state = draft.value
    if (!state || !state.myPosition) return null
    const targets = pool.value.slice(0, POOL_TARGETS)
    return {
      position: state.myPosition,
      plannedPick: plannedPick.value,
      pool: targets.map((championId, index) => ({ championId, weight: targets.length - index })),
      // Locked or hovered: an ally's intended pick is never a ban suggestion.
      allyChampions: state.myTeam
        .filter(slot => !slot.isMe && slot.championId !== null)
        .map(slot => slot.championId!),
      enemyChampions: state.enemyChampions,
      bans: [...state.allyBans, ...state.enemyBans],
    }
  })

  async function fetchBans() {
    const body = request.value
    if (!active.value || !body) {
      bans.value = null
      return
    }

    const requestId = ++latestRequest
    pending.value = true
    error.value = null
    try {
      const answer = await ask(body)
      if (requestId === latestRequest) bans.value = answer
    }
    catch (cause) {
      if (requestId === latestRequest) error.value = String(cause)
    }
    finally {
      if (requestId === latestRequest) pending.value = false
    }
  }

  const key = computed(() => JSON.stringify([active.value, request.value]))
  watch(key, () => {
    clearTimeout(timer)
    timer = setTimeout(fetchBans, DEBOUNCE_MS)
  }, { immediate: true })

  onScopeDispose(() => clearTimeout(timer))

  return { bans, pending, error }
}
