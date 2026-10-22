/** Mirrors `RankedQueue` in `crates/lcu/src/record.rs`. */
export interface RankedQueue {
  /** `RANKED_SOLO_5x5` or `RANKED_FLEX_SR`. */
  queueType: string
  /** `IRON` … `CHALLENGER`; empty or `NONE` while unranked. */
  tier: string
  /** `I` … `IV`; `NA` on the apex tiers. */
  division: string
  leaguePoints: number
  wins: number
  losses: number
  isProvisional: boolean
  provisionalGamesRemaining: number
}

/** Mirrors `PlayerGame` in `crates/lcu/src/record.rs`. */
export interface PlayerGame {
  gameId: number
  /** The player's slot in the game, to find them among `participants`. */
  participantId: number
  /** Epoch milliseconds. */
  playedAt: number
  durationSeconds: number
  queueId: number
  mapId: number
  championId: number
  championLevel: number
  /** 100 or 200 — the player's side among `participants`. */
  teamId: number
  /** `TOP` … `UTILITY`; null where the game had no lanes. */
  position: string | null
  win: boolean
  /** Ended before five minutes: shown, but kept out of every average. */
  remake: boolean
  kills: number
  deaths: number
  assists: number
  cs: number
  gold: number
  damageToChampions: number
  visionScore: number
  largestMultiKill: number
  /** Inventory slots 0..5, zero where empty. */
  items: number[]
  trinket: number
  /** Riot's role-bound slot (a bot laner's boots once the role quest is done), zero when empty. */
  roleBoundItem: number
  spells: [number, number]
  keystone: number
  primaryStyle: number
  subStyle: number
  /** Null when the game's scoreboard could not be read — unknown, not zero. */
  teamKills: number | null
  teamDamageToChampions: number | null
  /** Both teams, from the scoreboard; empty when it could not be read. */
  participants: GameParticipant[]
}

/** Mirrors `TimelineMark` in `crates/lcu/src/marks.rs`: one player at one of the timeline's canonical marks. */
export interface TimelineMark {
  minute: number
  gold: number
  cs: number
  xp: number
}

/**
 * Mirrors `GameParticipant` in `crates/lcu/src/record.rs`: who played what, and
 * the scoreboard line and timeline marks the performance score grades (#2075).
 */
export interface GameParticipant {
  participantId: number
  championId: number
  teamId: number
  position: string | null
  gameName: string | null
  tagLine: string | null
  win: boolean
  kills: number
  deaths: number
  assists: number
  /** Lane minions and jungle camps together. */
  cs: number
  goldEarned: number
  damageToChampions: number
  visionScore: number
  /** Ascending; empty when the game's timeline could not be read. */
  marks: TimelineMark[]
}

/** Mirrors `PlayerRecord` in `crates/lcu/src/record.rs`. */
export interface PlayerRecord {
  platformId: string | null
  ranked: RankedQueue[]
  backgroundSkinId: number | null
  /** Newest first. */
  games: PlayerGame[]
}
