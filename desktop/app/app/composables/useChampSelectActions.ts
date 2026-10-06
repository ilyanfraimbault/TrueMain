import type { InjectionKey, Ref } from 'vue'
import type { DraftState, TeamSlot } from '~/types/lcu'
import type { Lane } from '~/types/draft'
import { LANE_LABELS } from '~/types/draft'

/** The two writes the app makes to champion select — on a click, never on its own. */
export type ChampSelectKind = 'pick' | 'ban'

/** Whether a champion can be written right now, and if not, the reason the control shows. */
export interface Availability {
  allowed: boolean
  reason: string | null
}

interface Choices {
  /** The shell can write: a live client or the simulator, not a tape. */
  writable: boolean
  /** What the client lists; `null` while unknown (the shell checks again before writing). */
  pickable: number[] | null
  bannable: number[] | null
}

/** The shell's refusals (`lcu::champ_select::Refusal`, `champselect.rs`), in the player's words. */
const REFUSALS: Record<string, (champion: string) => { title: string, description: string }> = {
  'not-your-turn': () => ({ title: 'Not your turn', description: 'The client has no ban or pick of yours open right now. Nothing was sent.' }),
  'not-pickable': champion => ({ title: `${champion} is not available`, description: 'The client does not let you pick it: not owned, disabled or already taken.' }),
  'not-bannable': champion => ({ title: `${champion} cannot be banned`, description: 'It is already banned or picked.' }),
  'hover-mismatch': champion => ({ title: 'Your hover changed', description: `The client no longer shows ${champion} hovered. Nothing was locked.` }),
  'client-refused': () => ({ title: 'The client refused', description: 'Your turn probably ended, or the draft moved on. Nothing was changed.' }),
}

const KEY: InjectionKey<ReturnType<typeof create>> = Symbol('champ-select-actions')

/**
 * Hover, lock and ban from the draft screen (#1909), twin of `useRuneImport`:
 * every write is the player's own click, the shell re-checks it against the
 * client before sending, and every refusal is said in a toast — never retried.
 *
 * The draft screen creates it (`provideChampSelectActions`); the controls under
 * it share that one instance (`useChampSelectActions`).
 */
export function provideChampSelectActions(draft: Ref<DraftState>) {
  const actions = create(draft)
  provide(KEY, actions)
  return actions
}

export function useChampSelectActions() {
  const actions = inject(KEY, null)
  if (!actions) throw new Error('useChampSelectActions needs the draft screen above it')
  return actions
}

function create(draft: Ref<DraftState>) {
  const toast = useToast()
  const { nameOf } = useChampionStatics()

  // Outside the shell (the dev server's scenarios) the controls show and send nothing, like the rune import.
  const choices = ref<Choices>({ writable: !insideTauri() && import.meta.dev, pickable: null, bannable: null })
  /** The write in flight, so its control spins and no second one starts. */
  const pending = ref<{ kind: ChampSelectKind | 'lock', championId: number } | null>(null)

  const action = computed(() => draft.value.myAction?.inProgress ? draft.value.myAction : null)
  const planning = computed(() => draft.value.timerPhase === 'PLANNING' && draft.value.myNextAction?.kind === 'pick')

  /** What a click on a champion writes right now, if anything. */
  const kind = computed<ChampSelectKind | null>(() => {
    if (action.value?.kind === 'pick' || action.value?.kind === 'ban') return action.value.kind
    return planning.value ? 'pick' : null
  })

  /** What the control is labelled, even out of turn: the kind of our next action. */
  const shownKind = computed<ChampSelectKind>(() => kind.value ?? (draft.value.myNextAction?.kind === 'ban' ? 'ban' : 'pick'))

  /** The champion the client shows on our pick — the one "Lock in" commits. */
  const hovered = computed(() => {
    if (action.value?.kind === 'pick') return action.value.championId
    return planning.value ? draft.value.myNextAction?.championId ?? null : null
  })

  const canLock = computed(() => action.value?.kind === 'pick' && hovered.value !== null)

  /** Whose turn it is, for the strip over the draft. `null` when the draft is no longer about ours. */
  const turnLabel = computed(() => {
    if (action.value?.kind === 'ban') return 'Your ban'
    if (action.value?.kind === 'pick') return 'Your pick'
    if (planning.value) return 'Declare your pick'
    const next = draft.value.myNextAction
    const turns = draft.value.turnsUntilMyAction
    if (!next || turns === null) return null
    const what = next.kind === 'ban' ? 'Your ban' : 'Your pick'
    return turns <= 1 ? `${what} is next` : `${what} in ${turns} turns`
  })

  /** An ally showing `championId` as their pick intent, not yet locked. */
  function allyHovering(championId: number): TeamSlot | null {
    return draft.value.myTeam.find(slot => !slot.isMe && !slot.locked && slot.championId === championId) ?? null
  }

  /** "your Jungle", or "an ally" in queues without lanes. */
  function allyName(slot: TeamSlot) {
    const lane = LANE_LABELS[slot.position as Lane]
    return lane ? `your ${lane}` : 'an ally'
  }

  const banned = computed(() => new Set([...draft.value.allyBans, ...draft.value.enemyBans]))
  const picked = computed(() => new Set([
    ...draft.value.myTeam.filter(slot => slot.locked && slot.championId !== null).map(slot => slot.championId!),
    ...draft.value.enemyChampions,
  ]))

  function availability(championId: number): Availability {
    const blocked = (reason: string) => ({ allowed: false, reason })
    if (!kind.value) return blocked(turnLabel.value ? `Not your turn — ${turnLabel.value.toLowerCase()}` : 'Not your turn')
    if (banned.value.has(championId)) return blocked('Banned')
    if (picked.value.has(championId)) return blocked('Already picked')
    const listed = kind.value === 'pick' ? choices.value.pickable : choices.value.bannable
    if (listed && !listed.includes(championId)) return blocked(kind.value === 'pick' ? 'Not owned or disabled' : 'Cannot be banned')
    if (kind.value === 'pick' && hovered.value === championId) return blocked('Hovered in the client')
    return { allowed: true, reason: null }
  }

  async function send(command: 'champ_select_hover' | 'champ_select_lock', kind: ChampSelectKind, championId: number) {
    if (insideTauri()) {
      const { invoke } = await import('@tauri-apps/api/core')
      await invoke(command, { kind, championId })
    }
  }

  function refused(error: unknown, championId: number) {
    const message = String(error)
    const known = REFUSALS[message]
    toast.add(known
      ? { ...known(nameOf(championId)), icon: 'i-lucide-triangle-alert', color: 'warning' }
      : { title: 'Nothing was sent to the client', description: message, icon: 'i-lucide-circle-x', color: 'error' })
  }

  /** One write, from one click: no queue, no retry. */
  async function run(kind: ChampSelectKind | 'lock', championId: number, write: () => Promise<void>, done: string) {
    if (pending.value) return
    pending.value = { kind, championId }
    try {
      await write()
      if (!insideTauri()) toast.add({ title: done, description: 'Dev server: nothing was sent to a client.', icon: 'i-lucide-check', color: 'success' })
    }
    catch (error) {
      refused(error, championId)
    }
    finally {
      pending.value = null
      void refresh()
    }
  }

  /** Hover `championId` on our open pick or ban — the planning phase's intent included. */
  function hover(championId: number) {
    const current = kind.value
    if (!current) return
    return run(current, championId, () => send('champ_select_hover', current, championId), `${nameOf(championId)} hovered`)
  }

  /** Ban `championId`: hovered, then the ban completed — one confirmed click. */
  function ban(championId: number) {
    if (kind.value !== 'ban') return
    return run('ban', championId, async () => {
      await send('champ_select_hover', 'ban', championId)
      await send('champ_select_lock', 'ban', championId)
    }, `${nameOf(championId)} banned`)
  }

  /** Lock in the champion the client shows hovered on our pick. */
  function lock() {
    const championId = hovered.value
    if (!canLock.value || championId === null) return
    return run('lock', championId, () => send('champ_select_lock', 'pick', championId), `${nameOf(championId)} locked in`)
  }

  /** What the client lets the player pick and ban now; read again whenever the draft moves. */
  async function refresh() {
    if (!insideTauri()) return
    try {
      const { invoke } = await import('@tauri-apps/api/core')
      choices.value = await invoke<Choices>('champ_select_choices')
    }
    catch {
      choices.value = { ...choices.value, pickable: null, bannable: null }
    }
  }

  watch(() => [
    draft.value.myAction?.id,
    draft.value.myAction?.inProgress,
    draft.value.timerPhase,
    draft.value.allyBans.length + draft.value.enemyBans.length,
    picked.value.size,
  ].join(':'), refresh, { immediate: true })

  return { writable: computed(() => choices.value.writable), kind, shownKind, hovered, canLock, turnLabel, pending, availability, allyHovering, allyName, hover, ban, lock }
}
