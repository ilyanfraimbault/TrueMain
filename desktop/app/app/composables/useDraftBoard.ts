import type { Lane } from '~/types/draft'
import type { DraftState } from '~/types/lcu'
import { LANES } from '~/types/draft'

/** A champion select planned by hand: who is on which lane, on either side, and the bans. */
interface BoardState {
  myLane: Lane
  allies: Record<Lane, number | null>
  enemies: Record<Lane, number | null>
  allyBans: (number | null)[]
  enemyBans: (number | null)[]
}

/** A place on the board a champion can be put: a pick on a lane, or a ban. */
export type BoardSlot =
  | { kind: 'pick', team: 'ally' | 'enemy', lane: Lane }
  | { kind: 'ban', team: 'ally' | 'enemy', index: number }

const emptyLanes = (): Record<Lane, number | null> => Object.fromEntries(LANES.map(lane => [lane, null])) as Record<Lane, number | null>

function fresh(myLane: Lane = 'MIDDLE'): BoardState {
  return {
    myLane,
    allies: emptyLanes(),
    enemies: emptyLanes(),
    allyBans: Array(5).fill(null),
    enemyBans: Array(5).fill(null),
  }
}

/**
 * The champion select page when the client is not in one: a board the player
 * fills in any order — their lane, each side's picks on their lanes, the bans
 * — read by the same draft screen as a live one. No turns, no timer: it is a
 * draft to think through, not a champion select to act out.
 *
 * It yields the `DraftState` the client would push, with every placed pick
 * locked (a champion on the board is a decision), and the enemies' lanes as
 * placed, which the draft screen pins instead of guessing them.
 */
export function useDraftBoard() {
  const board = useState<BoardState>('draft-board', () => fresh())

  const championAt = (slot: BoardSlot): number | null => {
    const state = board.value
    if (slot.kind === 'pick') return (slot.team === 'ally' ? state.allies : state.enemies)[slot.lane]
    return (slot.team === 'ally' ? state.allyBans : state.enemyBans)[slot.index] ?? null
  }

  /** Put a champion on a slot — or clear it with `null`. A champion already elsewhere on the board moves. */
  function place(slot: BoardSlot, championId: number | null) {
    const next = structuredClone(toRaw(board.value))
    if (championId !== null) {
      for (const lane of LANES) {
        if (next.allies[lane] === championId) next.allies[lane] = null
        if (next.enemies[lane] === championId) next.enemies[lane] = null
      }
      next.allyBans = next.allyBans.map(id => (id === championId ? null : id))
      next.enemyBans = next.enemyBans.map(id => (id === championId ? null : id))
    }
    if (slot.kind === 'pick') (slot.team === 'ally' ? next.allies : next.enemies)[slot.lane] = championId
    else (slot.team === 'ally' ? next.allyBans : next.enemyBans)[slot.index] = championId
    board.value = next
  }

  function setMyLane(lane: Lane) {
    board.value = { ...board.value, myLane: lane }
  }

  /** An empty board, on the lane the player was on. */
  function clear() {
    board.value = fresh(board.value.myLane)
  }

  const isEmpty = computed(() => {
    const state = board.value
    return LANES.every(lane => state.allies[lane] === null && state.enemies[lane] === null)
      && [...state.allyBans, ...state.enemyBans].every(id => id === null)
  })

  /** Every champion on the board, picks and bans: none can be placed twice. */
  const placed = computed(() => {
    const state = board.value
    return new Set([...LANES.map(lane => state.allies[lane]), ...LANES.map(lane => state.enemies[lane]), ...state.allyBans, ...state.enemyBans]
      .filter((id): id is number => id !== null))
  })

  const draft = computed<DraftState>(() => {
    const state = board.value
    const mine = state.allies[state.myLane]
    const picked = (lanes: Record<Lane, number | null>) => LANES.map(lane => lanes[lane]).filter((id): id is number => id !== null)
    return {
      myPosition: state.myLane,
      myChampion: mine,
      myChampionLocked: mine !== null,
      allyChampions: picked(state.allies),
      enemyChampions: picked(state.enemies),
      myTeam: LANES.map(lane => ({
        championId: state.allies[lane],
        position: lane,
        locked: state.allies[lane] !== null,
        isMe: lane === state.myLane,
      })),
      allyBans: state.allyBans.filter((id): id is number => id !== null),
      enemyBans: state.enemyBans.filter((id): id is number => id !== null),
      secondsLeft: 0,
    }
  })

  /** The enemies' lanes as placed, for the draft screen to pin rather than guess. */
  const enemyLanes = computed(() => Object.fromEntries(LANES
    .filter(lane => board.value.enemies[lane] !== null)
    .map(lane => [board.value.enemies[lane]!, lane])) as Record<number, string>)

  return { board, draft, enemyLanes, placed, isEmpty, championAt, place, setMyLane, clear }
}
