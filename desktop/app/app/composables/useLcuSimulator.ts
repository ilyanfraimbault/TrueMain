import type { Lane } from '~/types/draft'
import { LANES } from '~/types/draft'

/**
 * The draft simulator's board — development only (`/dev/draft-sim`). A
 * champion select filled by clicking, in any order: our position, any pick on
 * either side, any ban. It yields what the League client would send for it,
 * `/lol-champ-select/v1/session` in the client's shape (`session`), which the
 * page relays to the app's shell.
 *
 * Our side takes the blue cells, 0-4 in lane order, so our cell is our lane's.
 * The enemy's five cells (5-9) carry no lane, as in the client — the app
 * guesses them. Our own pick is hovered first (`championPickIntent`) and
 * locked by hand; every other placed champion is locked.
 */

interface Pick {
  championId: number | null
  locked: boolean
}

interface Board {
  myLane: Lane
  allies: Record<Lane, Pick>
  enemies: (number | null)[]
  allyBans: (number | null)[]
  enemyBans: (number | null)[]
  /** The clock, restarted by every action like a phase of the client's. */
  secondsLeft: number
}

/** A place on the board a champion can go. */
export type SimSlot =
  | { kind: 'ally', lane: Lane }
  | { kind: 'enemy', index: number }
  | { kind: 'ban', team: 'ally' | 'enemy', index: number }

const PHASE_SECONDS = 30

const empty = (): Pick => ({ championId: null, locked: false })

function fresh(myLane: Lane = 'MIDDLE'): Board {
  return {
    myLane,
    allies: Object.fromEntries(LANES.map(lane => [lane, empty()])) as Record<Lane, Pick>,
    enemies: Array(5).fill(null),
    allyBans: Array(5).fill(null),
    enemyBans: Array(5).fill(null),
    secondsLeft: PHASE_SECONDS,
  }
}

export function useLcuSimulator() {
  const board = useState<Board>('lcu-simulator', () => fresh())

  const championAt = (slot: SimSlot): number | null => {
    const state = board.value
    if (slot.kind === 'ally') return state.allies[slot.lane].championId
    if (slot.kind === 'enemy') return state.enemies[slot.index] ?? null
    return (slot.team === 'ally' ? state.allyBans : state.enemyBans)[slot.index] ?? null
  }

  /** Every champion picked or banned: none can be placed twice. */
  const taken = computed(() => {
    const state = board.value
    return new Set([...LANES.map(lane => state.allies[lane].championId), ...state.enemies, ...state.allyBans, ...state.enemyBans]
      .filter((id): id is number => id !== null))
  })

  /** Put a champion on a slot, or clear it with `null`. Our own pick lands hovered; the rest lock at once. */
  function place(slot: SimSlot, championId: number | null) {
    // A JSON copy: the state is plain data, and `structuredClone` refuses the reactive proxies nested in it.
    const next: Board = JSON.parse(JSON.stringify(board.value))
    if (slot.kind === 'ally') {
      const mine = slot.lane === next.myLane
      next.allies[slot.lane] = { championId, locked: championId !== null && !mine }
    }
    else if (slot.kind === 'enemy') {
      next.enemies[slot.index] = championId
    }
    else {
      (slot.team === 'ally' ? next.allyBans : next.enemyBans)[slot.index] = championId
    }
    next.secondsLeft = PHASE_SECONDS
    board.value = next
  }

  function lockMine() {
    const mine = board.value.allies[board.value.myLane]
    if (mine.championId === null || mine.locked) return
    board.value = { ...board.value, allies: { ...board.value.allies, [board.value.myLane]: { ...mine, locked: true } }, secondsLeft: PHASE_SECONDS }
  }

  /** Our position moves; a champion on the old lane stays there, as an ally's. */
  function setMyLane(lane: Lane) {
    board.value = { ...board.value, myLane: lane }
  }

  function clear() {
    board.value = fresh(board.value.myLane)
  }

  function tick() {
    if (board.value.secondsLeft > 0) board.value = { ...board.value, secondsLeft: board.value.secondsLeft - 1 }
  }

  const session = computed(() => {
    const state = board.value
    const myCell = LANES.indexOf(state.myLane)
    const mine = state.allies[state.myLane]
    const bansOf = (team: 'ally' | 'enemy') => (team === 'ally' ? state.allyBans : state.enemyBans)
    const banActions = (['ally', 'enemy'] as const).flatMap(team => bansOf(team).map((championId, index) => ({
      actorCellId: (team === 'ally' ? 0 : 5) + index,
      championId: championId ?? 0,
      completed: championId !== null,
      isAllyAction: team === 'ally',
      isInProgress: false,
      type: 'ban',
    })))
    return {
      localPlayerCellId: myCell,
      myTeam: LANES.map((lane, cell) => ({
        cellId: cell,
        championId: state.allies[lane].locked ? state.allies[lane].championId ?? 0 : 0,
        championPickIntent: state.allies[lane].locked ? 0 : state.allies[lane].championId ?? 0,
        assignedPosition: lane.toLowerCase(),
      })),
      theirTeam: state.enemies.map((championId, index) => ({ cellId: 5 + index, championId: championId ?? 0, championPickIntent: 0, assignedPosition: '' })),
      bans: {
        myTeamBans: state.allyBans.filter((id): id is number => id !== null),
        theirTeamBans: state.enemyBans.filter((id): id is number => id !== null),
      },
      actions: [
        ...banActions.map(action => [action]),
        [{ actorCellId: myCell, championId: mine.championId ?? 0, completed: mine.locked, isAllyAction: true, isInProgress: !mine.locked, type: 'pick' }],
      ],
      timer: { adjustedTimeLeftInPhase: state.secondsLeft * 1000, phase: 'BAN_PICK' },
    }
  })

  return { board, taken, championAt, place, lockMine, setMyLane, clear, tick, session }
}
