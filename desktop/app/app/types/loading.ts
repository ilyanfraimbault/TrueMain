import type { GameTeam } from '~/types/game'
import type { RankedQueue } from '~/types/record'

/** Mirrors `PlayerForm` in `crates/lcu/src/form.rs`. */
export interface PlayerForm {
  /** Summoner's Rift games read (up to 20), remakes left out. */
  games: number
  /** Of those, the ones on the champion the player is on now. */
  championGames: number
  championWins: number
  /** Kills, deaths and assists summed over the games on the champion. */
  championKills: number
  championDeaths: number
  championAssists: number
  /** The roles the player was given, most played first, over role-assigned queues. */
  positions: PositionGames[]
  /** The run the latest games make: 3 for three wins in a row, -2 for two losses. */
  streak: number
  /** The latest of those, newest first, at most ten. */
  recent: RecentGame[]
}

/** Mirrors `PositionGames` in `crates/lcu/src/form.rs`. */
export interface PositionGames {
  position: string
  games: number
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
  /** Solo/Duo, else a ranked Flex; null until read, when unranked, or unreadable. */
  rank: RankedQueue | null
  /** The standing was read: a null `rank` is then an unranked player. */
  rankRead: boolean
  /** The client could not read the standing. */
  rankFailed: boolean
}

export interface LoadingView {
  players: LoadingPlayer[]
}
