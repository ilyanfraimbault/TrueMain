import type { GameTeam } from '~/types/game'

/** Mirrors `PlayerForm` in `crates/lcu/src/form.rs`. */
export interface PlayerForm {
  /** Summoner's Rift games read (up to 20), remakes left out. */
  games: number
  /** Of those, the ones on the champion the player is on now. */
  championGames: number
  championWins: number
  /** The latest of those, newest first, at most ten. */
  recent: RecentGame[]
}

/** Mirrors `RecentGame` in `crates/lcu/src/form.rs`. */
export interface RecentGame {
  championId: number
  /** The assigned role; null on a queue without roles. */
  position: string | null
  win: boolean
  kills: number
  deaths: number
  assists: number
  /** Epoch milliseconds. */
  playedAt: number
}

/** Mirrors `LoadingPlayer` in `src-tauri/src/loading.rs`. */
export interface LoadingPlayer {
  /** Empty for an anonymous player. */
  riotId: string
  championId: number
  team: GameTeam
  position: string
  isMe: boolean
  /** The player hides their name (Streamer Mode): only their champion shows. */
  anonymous: boolean
  /** Absent until read, and for good when the client could not read it. */
  form: PlayerForm | null
  failed: boolean
}

export interface LoadingView {
  players: LoadingPlayer[]
}
