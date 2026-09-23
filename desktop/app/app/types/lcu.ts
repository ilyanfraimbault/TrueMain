/** Mirrors `AppState` in `crates/shell-state/src/lib.rs`. */
export interface DraftState {
  /** Our lane, upper-case (`MIDDLE`); empty in queues that assign none. */
  myPosition: string
  myChampion: number | null
  myChampionLocked: boolean
  allyChampions: number[]
  /** Enemies picked or hovered so far — fewer than five for most of the draft. */
  enemyChampions: number[]
  /** Every cell of our side, us included, empty ones kept, in cell order. */
  myTeam: TeamSlot[]
  allyBans: number[]
  enemyBans: number[]
  secondsLeft: number
}

/** Mirrors `TeamSlot` in `crates/lcu/src/model.rs`. */
export interface TeamSlot {
  championId: number | null
  /** Upper-case lane, empty in queues that assign none. */
  position: string
  locked: boolean
  isMe: boolean
}

export type GameflowPhase =
  | 'None' | 'Lobby' | 'Matchmaking' | 'ReadyCheck' | 'ChampSelect'
  | 'InProgress' | 'Reconnect' | 'WaitingForStats' | 'PreEndOfGame' | 'EndOfGame' | 'Unknown'

export interface AppState {
  /** False means no client is running — a normal state, not an error. */
  connected: boolean
  phase: GameflowPhase
  riotId: string | null
  profileIconId: number | null
  summonerLevel: number | null
  draft: DraftState | null
}

/** Mirrors `Screen` in `crates/shell-state/src/lib.rs`. */
export type Screen = 'no-client' | 'dashboard' | 'draft' | 'in-game'

export const EMPTY_STATE: AppState = {
  connected: false,
  phase: 'None',
  riotId: null,
  profileIconId: null,
  summonerLevel: null,
  draft: null,
}
