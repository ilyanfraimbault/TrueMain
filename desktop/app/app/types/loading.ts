import type { GameTeam } from '~/types/game'

/** Mirrors `PlayerForm` in `crates/lcu/src/form.rs`. */
export interface PlayerForm {
  /** Summoner's Rift games read (up to 20), remakes left out. */
  games: number
  /** Of those, the ones on the champion the player is on now. */
  championGames: number
  championWins: number
  /** Ranked games won (positive) or lost (negative) in a row; 0 with none read. */
  streak: number
}

/** Mirrors `LoadingPlayer` in `src-tauri/src/loading.rs`. */
export interface LoadingPlayer {
  riotId: string
  championId: number
  team: GameTeam
  position: string
  isMe: boolean
  /** Absent until read, and for good when the client could not read it. */
  form: PlayerForm | null
  failed: boolean
}

export interface LoadingView {
  players: LoadingPlayer[]
}
