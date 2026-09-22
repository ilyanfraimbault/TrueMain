/** Mirrors `AppState` in `crates/shell-state/src/lib.rs`. */
export interface DraftState {
  /** Our lane, upper-case (`MIDDLE`); empty in queues that assign none. */
  myPosition: string
  myChampion: number | null
  myChampionLocked: boolean
  allyChampions: number[]
  /** Enemies picked or hovered so far — fewer than five for most of the draft. */
  enemyChampions: number[]
  bans: number[]
  secondsLeft: number
}

export type GameflowPhase =
  | 'None' | 'Lobby' | 'Matchmaking' | 'ReadyCheck' | 'ChampSelect'
  | 'InProgress' | 'Reconnect' | 'WaitingForStats' | 'PreEndOfGame' | 'EndOfGame' | 'Unknown'

export interface AppState {
  /** False means no client is running — a normal state, not an error. */
  connected: boolean
  phase: GameflowPhase
  riotId: string | null
  draft: DraftState | null
}

/** Mirrors `Screen` in `crates/shell-state/src/lib.rs`. */
export type Screen = 'no-client' | 'dashboard' | 'draft' | 'in-game'

export const EMPTY_STATE: AppState = {
  connected: false,
  phase: 'None',
  riotId: null,
  draft: null,
}
