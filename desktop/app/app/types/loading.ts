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

/** Mirrors `LaningForm` in `crates/lcu/src/laning.rs`. */
export interface LaningForm {
  /** Games on the champion weighed, at most ten. */
  games: number
  wins: number
  /** Of those, the ones whose fifteen-minute gaps were read. */
  measured: number
  /** Average leads over the lane opponent at fifteen minutes; null with none measured. */
  goldDiff15: number | null
  csDiff15: number | null
  xpDiff15: number | null
}

/**
 * Mirrors `Read<T>` in `src-tauri/src/loading/view.rs`: one thing read about a
 * player — asked and not answered yet, read, or failed for good.
 */
export type Read<T> =
  | { state: 'pending' }
  | { state: 'ok', value: T }
  | { state: 'failed' }

/** The value of a read once it is in; null while pending or failed. */
export function readValue<T>(read: Read<T>): T | null {
  return read.state === 'ok' ? read.value : null
}

/** Mirrors `LoadingStatus`: where the board stands. */
export type LoadingStatus =
  /** No game is loading or running. */
  | 'idle'
  /** The roster or its players' reads are under way. */
  | 'reading'
  /** Every read answered or failed: nothing more is coming. */
  | 'done'
  /** The client never listed the game's players. */
  | 'rosterFailed'

interface LoadingSeat {
  championId: number
  team: GameTeam
  position: string
  isMe: boolean
}

/** Mirrors `Known`: a player whose name and history are read. */
export interface KnownPlayer extends LoadingSeat {
  kind: 'known'
  /** Empty until read, when the session left it out. */
  riotId: string
  form: Read<PlayerForm>
  /** Solo/Duo, else a ranked Flex; `ok` with null for an unranked player. */
  rank: Read<RankedQueue | null>
  /** Their form on the champion (#1863): read once their history is, failed with it. */
  laning: Read<LaningForm>
}

/** Mirrors `Identity::Hidden`: the player hides their name (Streamer Mode); only their champion and lane show. */
export interface HiddenPlayer extends LoadingSeat {
  kind: 'hidden'
}

/** Mirrors `LoadingPlayer` in `src-tauri/src/loading/view.rs`. */
export type LoadingPlayer = KnownPlayer | HiddenPlayer

/** Mirrors `LoadingView` in `src-tauri/src/loading/view.rs`. */
export interface LoadingView {
  status: LoadingStatus
  players: LoadingPlayer[]
  /** The shard the game is played on, e.g. `EUW1`; empty until the first history is read. */
  platformId: string
}

export const IDLE_LOADING: LoadingView = { status: 'idle', players: [], platformId: '' }
