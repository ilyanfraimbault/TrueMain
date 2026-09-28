import type { Lane } from '~/types/draft'
import type { DraftState } from '~/types/lcu'
import { LANES, LANE_LABELS } from '~/types/draft'

export type Side = 'blue' | 'red'

/** One action of the draft: a ban or a pick, by one side, for one of its five cells. */
export interface SimStep {
  kind: 'ban' | 'pick'
  team: 'ally' | 'enemy'
  cell: number
}

interface SimSlot {
  championId: number | null
  locked: boolean
}

interface SimState {
  side: Side
  myLane: Lane
  /** Our cell, 0..4 — which of our five picks is ours. */
  myCell: number
  allyBans: (number | null)[]
  enemyBans: (number | null)[]
  ally: SimSlot[]
  enemy: SimSlot[]
  /** The lane each enemy cell will play, for the auto-fill only — the draft never sees it. */
  enemyPlan: Lane[]
  step: number
  secondsLeft: number
}

/** The client's timer for every action of a ranked draft. */
const STEP_SECONDS = 30

/** The pick order of a ranked draft, blue first: 1-2-2-2-2-1. */
const PICK_ORDER: Side[] = ['blue', 'red', 'red', 'blue', 'blue', 'red', 'red', 'blue', 'blue', 'red']

/**
 * A ranked draft's actions in order. Both sides ban at once in solo queue;
 * here ours come first, then theirs, so each is one step of its own.
 */
export function draftSequence(side: Side): SimStep[] {
  const cells = [0, 1, 2, 3, 4]
  const bans: SimStep[] = [
    ...cells.map(cell => ({ kind: 'ban' as const, team: 'ally' as const, cell })),
    ...cells.map(cell => ({ kind: 'ban' as const, team: 'enemy' as const, cell })),
  ]
  const next = { ally: 0, enemy: 0 }
  const picks = PICK_ORDER.map((turn) => {
    const team = turn === side ? 'ally' as const : 'enemy' as const
    return { kind: 'pick' as const, team, cell: next[team]++ }
  })
  return [...bans, ...picks]
}

const shuffle = <T>(list: T[]): T[] => list
  .map(value => ({ value, key: Math.random() }))
  .sort((a, b) => a.key - b.key)
  .map(entry => entry.value)

function fresh(side: Side = 'blue', myLane: Lane = 'MIDDLE', myCell = 2): SimState {
  return {
    side,
    myLane,
    myCell,
    allyBans: Array(5).fill(null),
    enemyBans: Array(5).fill(null),
    ally: Array.from({ length: 5 }, () => ({ championId: null, locked: false })),
    enemy: Array.from({ length: 5 }, () => ({ championId: null, locked: false })),
    enemyPlan: shuffle([...LANES]),
    step: 0,
    secondsLeft: STEP_SECONDS,
  }
}

/**
 * A champion select played by hand, for development: the draft simulator page
 * (`/dev/draft-sim`) drives it one ban and one pick at a time, in the client's
 * order, and sends what the client would — the session as
 * `/lol-champ-select/v1/session` returns it (`lcuSession`) — to the app's shell
 * through the dev server's relay. The app then goes through its real champion
 * select, parsing included, without a game.
 *
 * Kept in app state, so leaving the page and coming back finds the draft where
 * it was.
 */
export function useDraftSimulator() {
  const sim = useState<SimState>('draft-simulator', () => fresh())
  const { laneEntries, entries } = useTierList()

  const steps = computed(() => draftSequence(sim.value.side))
  const current = computed<SimStep | null>(() => steps.value[sim.value.step] ?? null)
  const done = computed(() => current.value === null)
  const isMyTurn = computed(() => current.value?.kind === 'pick' && current.value.team === 'ally' && current.value.cell === sim.value.myCell)

  /** The lane each of our cells holds: ours where we are, the rest in lane order. */
  const allyLanes = computed<Lane[]>(() => {
    const others = LANES.filter(lane => lane !== sim.value.myLane)
    return Array.from({ length: 5 }, (_, cell) => (cell === sim.value.myCell ? sim.value.myLane : others.shift()!))
  })

  /** Every champion already banned or on the board. */
  const taken = computed(() => new Set([
    ...sim.value.allyBans, ...sim.value.enemyBans,
    ...sim.value.ally.map(slot => slot.championId), ...sim.value.enemy.map(slot => slot.championId),
  ].filter((id): id is number => id !== null)))

  /** The lane the current step is for, when there is one to rank by. */
  const stepLane = computed<Lane | null>(() => {
    const step = current.value
    if (step?.kind !== 'pick') return null
    return step.team === 'ally' ? allyLanes.value[step.cell]! : sim.value.enemyPlan[step.cell]!
  })

  const label = computed(() => {
    const step = current.value
    if (!step) return 'Draft complete'
    if (step.kind === 'ban') return step.team === 'ally' ? `Your team bans · ${step.cell + 1}/5` : `Enemy bans · ${step.cell + 1}/5`
    if (isMyTurn.value) return sim.value.ally[step.cell]!.championId ? 'Your pick · hovering' : 'Your pick'
    return step.team === 'ally' ? `Ally pick · ${LANE_LABELS[allyLanes.value[step.cell]!]}` : `Enemy pick ${step.cell + 1}`
  })

  function advance() {
    sim.value.step++
    sim.value.secondsLeft = STEP_SECONDS
  }

  /** Fill the current step. Our own pick can be hovered first, as in the client. */
  function choose(championId: number, lock = true) {
    const step = current.value
    if (!step) return
    // Taken is taken — except the champion we are hovering, which we may lock.
    const hovering = isMyTurn.value && sim.value.ally[step.cell]!.championId === championId
    if (taken.value.has(championId) && !hovering) return
    if (step.kind === 'ban') {
      (step.team === 'ally' ? sim.value.allyBans : sim.value.enemyBans)[step.cell] = championId
      advance()
      return
    }
    const slot = (step.team === 'ally' ? sim.value.ally : sim.value.enemy)[step.cell]!
    const hover = !lock && isMyTurn.value
    slot.championId = championId
    slot.locked = !hover
    if (!hover) advance()
  }

  /** Lock in our hovered champion. */
  function lockIn() {
    const step = current.value
    const slot = step ? sim.value.ally[step.cell] : null
    if (!isMyTurn.value || !slot?.championId) return
    slot.locked = true
    advance()
  }

  /** A ban can be passed, as in the client. */
  function skip() {
    if (current.value?.kind === 'ban') advance()
  }

  function undo() {
    const step = current.value
    // A hover is the first thing taken back.
    if (step && isMyTurn.value && sim.value.ally[step.cell]!.championId) {
      sim.value.ally[step.cell] = { championId: null, locked: false }
      return
    }
    if (sim.value.step === 0) return
    sim.value.step--
    const previous = steps.value[sim.value.step]!
    if (previous.kind === 'ban') (previous.team === 'ally' ? sim.value.allyBans : sim.value.enemyBans)[previous.cell] = null
    else (previous.team === 'ally' ? sim.value.ally : sim.value.enemy)[previous.cell] = { championId: null, locked: false }
    sim.value.secondsLeft = STEP_SECONDS
  }

  function reset(options: Partial<Pick<SimState, 'side' | 'myLane' | 'myCell'>> = {}) {
    const { side, myLane, myCell } = sim.value
    sim.value = fresh(options.side ?? side, options.myLane ?? myLane, options.myCell ?? myCell)
  }

  /** A plausible champion for a step: a popular ban, or a meta pick for the lane. */
  function plausible(step: SimStep): number | null {
    const pool = step.kind === 'ban'
      ? [...entries.value].sort((a, b) => b.banRate - a.banRate).map(entry => entry.championId)
      : laneEntries(step.team === 'ally' ? allyLanes.value[step.cell]! : sim.value.enemyPlan[step.cell]!).map(entry => entry.championId)
    const open = [...new Set(pool)].filter(id => !taken.value.has(id)).slice(0, 12)
    return shuffle(open)[0] ?? null
  }

  /** Play the draft forward: up to our pick, or — at our pick — to the end. */
  function autofill() {
    const toEnd = isMyTurn.value
    // Twenty actions at most: a guard against a step that would not advance.
    for (let guard = 0; guard < 20 && current.value && (toEnd || !isMyTurn.value); guard++) {
      const step = current.value
      const mine = isMyTurn.value && sim.value.ally[step.cell]!.championId
      if (mine) {
        lockIn()
        continue
      }
      const champion = plausible(step)
      if (champion === null) break
      choose(champion)
    }
  }

  /** The draft as the client would push it — the one shape the draft screen reads. */
  const draft = computed<DraftState>(() => {
    const { ally, enemy, myCell, myLane } = sim.value
    const me = ally[myCell]!
    const picked = (slots: SimSlot[]) => slots.map(slot => slot.championId).filter((id): id is number => id !== null)
    return {
      myPosition: myLane,
      myChampion: me.championId,
      myChampionLocked: me.locked,
      allyChampions: picked(ally.filter((_, cell) => cell !== myCell)),
      enemyChampions: picked(enemy),
      myTeam: ally.map((slot, cell) => ({ championId: slot.championId, position: allyLanes.value[cell]!, locked: slot.locked, isMe: cell === myCell })),
      allyBans: sim.value.allyBans.filter((id): id is number => id !== null),
      enemyBans: sim.value.enemyBans.filter((id): id is number => id !== null),
      secondsLeft: done.value ? 0 : sim.value.secondsLeft,
    }
  })

  /** The slot being filled, for the draft screen to light. */
  const activeSlot = computed(() => {
    const step = current.value
    if (step?.kind !== 'pick') return null
    return step.team === 'ally' ? { team: 'ally' as const, lane: allyLanes.value[step.cell]! } : { team: 'enemy' as const }
  })

  /** Cell ids as the client numbers them: the blue side's are 0-4, the red side's 5-9. */
  const cellBase = (team: 'ally' | 'enemy') => ((team === 'ally') === (sim.value.side === 'blue') ? 0 : 5)

  /** The champion a step put down, if it has. */
  function championOf(step: SimStep): number | null {
    const state = sim.value
    if (step.kind === 'ban') return (step.team === 'ally' ? state.allyBans : state.enemyBans)[step.cell] ?? null
    return (step.team === 'ally' ? state.ally : state.enemy)[step.cell]?.championId ?? null
  }

  /**
   * The session as the client returns it (`/lol-champ-select/v1/session`),
   * with the fields the app reads: a hovered champion is `championPickIntent`
   * and a locked one `championId`, the enemy's lanes are not given, their
   * hovers are not shown, and every ban and pick is an action in draft order.
   */
  const lcuSession = computed(() => {
    const state = sim.value
    const nonNull = (ids: (number | null)[]) => ids.filter((id): id is number => id !== null)
    return {
      localPlayerCellId: cellBase('ally') + state.myCell,
      myTeam: state.ally.map((slot, cell) => ({
        cellId: cellBase('ally') + cell,
        championId: slot.locked ? slot.championId ?? 0 : 0,
        championPickIntent: slot.locked ? 0 : slot.championId ?? 0,
        assignedPosition: allyLanes.value[cell]!.toLowerCase(),
      })),
      theirTeam: state.enemy.map((slot, cell) => ({
        cellId: cellBase('enemy') + cell,
        championId: slot.locked ? slot.championId ?? 0 : 0,
        championPickIntent: 0,
        assignedPosition: '',
      })),
      bans: { myTeamBans: nonNull(state.allyBans), theirTeamBans: nonNull(state.enemyBans) },
      actions: steps.value.map((step, index) => [{
        actorCellId: cellBase(step.team) + step.cell,
        championId: championOf(step) ?? 0,
        completed: index < state.step,
        isAllyAction: step.team === 'ally',
        isInProgress: index === state.step,
        type: step.kind,
      }]),
      timer: {
        adjustedTimeLeftInPhase: done.value ? 0 : state.secondsLeft * 1000,
        phase: done.value ? 'FINALIZATION' : 'BAN_PICK',
      },
    }
  })

  /** The clock runs while the page is open; it only counts down, never acts. */
  function tick() {
    if (!done.value && sim.value.secondsLeft > 0) sim.value.secondsLeft--
  }

  return { sim, steps, current, done, isMyTurn, taken, stepLane, label, draft, lcuSession, activeSlot, allyLanes, choose, lockIn, skip, undo, reset, autofill, tick }
}
