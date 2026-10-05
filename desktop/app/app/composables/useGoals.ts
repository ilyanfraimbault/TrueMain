import type { Goal, GoalDraft, GoalEvaluation } from '~/utils/goals'
import type { QueueFilter } from '~/utils/player-form'
import { MAX_ACTIVE_GOALS, evaluate, suggestGoals } from '~/utils/goals'
import { readGoals, saveGoals, trimGoals } from '~/utils/goal-store'

/**
 * The logged-in player's goals (`utils/goals`), kept on this machine per Riot
 * ID (`utils/goal-store`) and measured against the games `usePlayerRecord`
 * reads — so they move after each game, the record being read again at the
 * end-of-game screen, and count the games played while the app was closed.
 * Each read freezes the newly counted games into the goal; older pages are read
 * only while an active goal's run reaches past the ones already held. In
 * `npm run dev` outside Tauri the dev scenario's goals stand in, held in memory.
 * Nothing here is sent to TrueMain.
 *
 * Called once, by the dashboard: it owns the watchers.
 */
export function useGoals() {
  const { state } = useLcuState()
  const { games, hasOlder, loadingOlder, loadOlder } = usePlayerRecord()
  const goals = useState<Goal[]>('player-goals', () => [])
  const owner = useState<string | null>('player-goals-owner', () => null)

  const persisted = () => insideTauri() || !import.meta.dev

  function store(next: Goal[]) {
    const riotId = owner.value
    if (!riotId) return
    goals.value = persisted() ? saveGoals(riotId, next) : trimGoals(next)
  }

  watch(() => state.value.riotId, (riotId) => {
    owner.value = riotId ?? null
    if (!riotId) goals.value = []
    else goals.value = persisted() ? readGoals(riotId) : [...useDevScenarios().goals.value]
  }, { immediate: true })

  const evaluations = computed(() => new Map<string, GoalEvaluation>(
    goals.value.map(goal => [goal.id, evaluate(goal, games.value, hasOlder.value)]),
  ))

  // Freeze what each read counted, and page back while a run needs it.
  watch(evaluations, (map) => {
    let changed = false
    const next = goals.value.map((goal) => {
      const evaluation = map.get(goal.id)
      if (!evaluation || goal.status !== 'active') return goal
      if (evaluation.counted === goal.results.length && evaluation.status === goal.status) return goal
      changed = true
      return { ...goal, results: evaluation.results, status: evaluation.status }
    })
    if (changed) store(next)
    if ([...map.values()].some(evaluation => evaluation.needsOlder) && !loadingOlder.value) void loadOlder()
  })

  const active = computed(() => goals.value.filter(goal => goal.status === 'active'))
  const canAdd = computed(() => active.value.length < MAX_ACTIVE_GOALS)

  function create(draft: GoalDraft) {
    const riotId = owner.value
    if (!riotId || !canAdd.value) return
    const id = globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(36).slice(2)}`
    store([...goals.value, { ...draft, id, riotId, createdAt: Date.now(), status: 'active', results: [] }])
  }

  function abandon(id: string) {
    store(goals.value.map(goal => (goal.id === id && goal.status === 'active' ? { ...goal, status: 'abandoned' } : goal)))
  }

  const suggestions = (filter: QueueFilter) => suggestGoals(games.value, active.value, filter)

  return { goals, evaluations, active, canAdd, create, abandon, suggestions }
}
