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
 *
 * It also answers the app's champion select requests as the client would
 * (#1909, `answer`): the session, the pickable and bannable lists, a hover and
 * a lock on our own ban or pick — refused unless the "our turn" control has
 * that action open, like a client whose turn is elsewhere.
 */

interface Pick {
  championId: number | null
  locked: boolean
}

/** Which of our actions the client has open: none, our ban, or our pick. */
export type SimTurn = 'none' | 'ban' | 'pick'

interface Board {
  myLane: Lane
  turn: SimTurn
  /** Our ban hovered and not yet completed. Our ban is the ally ban at our cell's index. */
  myBanHover: number | null
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

/** Action ids as the session lists them: bans by cell (1-10), our pick after them. */
const banActionId = (cell: number) => cell + 1
const pickActionId = (cell: number) => 100 + cell

/** One request of the app's shell, relayed by the dev server (`server/routes/__sim/lcu.ts`). */
export interface SimRequest {
  id: number
  method: string
  path: string
  body: { championId?: number } | null
}

const empty = (): Pick => ({ championId: null, locked: false })

function fresh(myLane: Lane = 'MIDDLE'): Board {
  return {
    myLane,
    turn: 'pick',
    myBanHover: null,
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
    board.value = { ...board.value, myLane: lane, myBanHover: null }
  }

  function setTurn(turn: SimTurn) {
    board.value = { ...board.value, turn, secondsLeft: PHASE_SECONDS }
  }

  /**
   * The client's answer to one request of the app, applied to the board as the
   * client would apply it. `champions` is every champion id there is: the
   * pickable and bannable lists are those not taken.
   */
  function answer(request: SimRequest, champions: number[], inChampSelect: boolean): { status: number, body: unknown } {
    const state = board.value
    const myCell = LANES.indexOf(state.myLane)
    const mine = state.allies[state.myLane]
    const refused = (status: number, message: string) => ({ status, body: { message } })
    if (!inChampSelect) return refused(404, 'No active delegate')

    if (request.method === 'GET') {
      if (request.path === '/lol-champ-select/v1/session') return { status: 200, body: session.value }
      if (request.path.endsWith('/pickable-champion-ids') || request.path.endsWith('/bannable-champion-ids')) {
        return { status: 200, body: champions.filter(id => !taken.value.has(id)) }
      }
      return refused(404, `Nothing at ${request.path}`)
    }

    const match = request.path.match(/^\/lol-champ-select\/v1\/session\/actions\/(\d+)(\/complete)?$/)
    if (!match) return refused(404, `Nothing at ${request.path}`)
    const id = Number(match[1])
    const complete = match[2] !== undefined
    const banOpen = state.turn === 'ban' && state.allyBans[myCell] === null
    const pickOpen = state.turn === 'pick' && !mine.locked

    if (id === banActionId(myCell) && banOpen) {
      if (!complete && request.method === 'PATCH') {
        board.value = { ...state, myBanHover: request.body?.championId ?? null }
        return { status: 204, body: null }
      }
      if (complete && request.method === 'POST' && state.myBanHover !== null) {
        const allyBans = [...state.allyBans]
        allyBans[myCell] = state.myBanHover
        board.value = { ...state, allyBans, myBanHover: null, secondsLeft: PHASE_SECONDS }
        return { status: 204, body: null }
      }
    }
    if (id === pickActionId(myCell) && pickOpen) {
      if (!complete && request.method === 'PATCH') {
        place({ kind: 'ally', lane: state.myLane }, request.body?.championId ?? null)
        return { status: 204, body: null }
      }
      if (complete && request.method === 'POST' && mine.championId !== null) {
        lockMine()
        return { status: 204, body: null }
      }
    }
    return refused(500, 'This action is not in progress')
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
    const banActions = (['ally', 'enemy'] as const).flatMap(team => bansOf(team).map((championId, index) => {
      const cell = (team === 'ally' ? 0 : 5) + index
      const ours = cell === myCell
      return {
        id: banActionId(cell),
        actorCellId: cell,
        championId: championId ?? (ours ? state.myBanHover ?? 0 : 0),
        completed: championId !== null,
        isAllyAction: team === 'ally',
        isInProgress: ours && championId === null && state.turn === 'ban',
        type: 'ban',
      }
    }))
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
        [{ id: pickActionId(myCell), actorCellId: myCell, championId: mine.championId ?? 0, completed: mine.locked, isAllyAction: true, isInProgress: !mine.locked && state.turn === 'pick', type: 'pick' }],
      ],
      timer: { adjustedTimeLeftInPhase: state.secondsLeft * 1000, phase: 'BAN_PICK' },
    }
  })

  return { board, taken, championAt, place, lockMine, setMyLane, setTurn, clear, tick, session, answer }
}
