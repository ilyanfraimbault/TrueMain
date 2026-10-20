// Mirrors backend/Api/ReadModels/Truemains/LiveGameReadModel.cs (#2103).

import type { ProfileRanked } from './profile'

/** `unavailable`: Riot could not be read (rate limited, down) — nothing is known, which is not "not in a game". */
export type LiveGameStatus = 'inGame' | 'notInGame' | 'unavailable'

export interface LiveGameResponse {
  status: LiveGameStatus
  /** When Riot was last asked; a cached answer keeps the time of the call it came from. */
  checkedAtUtc: string
  /** Only when `status` is `inGame`. */
  game: LiveGame | null
}

export interface LiveGame {
  gameId: number
  platformId: string
  queueId: number
  mapId: number
  gameMode: string
  /** Null while the players are on the loading screen. */
  startedAtUtc: string | null
  /** Seconds of game time when Riot answered. */
  gameLengthSeconds: number
  /** Blue side (100) first, then red (200). */
  teams: LiveTeam[]
}

export interface LiveTeam {
  teamId: number
  /** Banned champion ids, in pick-turn order. */
  bans: number[]
  /** Lane order (top to support) when the lanes could be inferred. */
  participants: LiveParticipant[]
}

export interface LiveParticipant {
  /** Null for a bot, or a hidden Riot ID. */
  gameName: string | null
  tagLine: string | null
  championId: number
  profileIconId: number
  spell1Id: number
  spell2Id: number
  keystoneId: number | null
  primaryStyleId: number | null
  secondaryStyleId: number | null
  /** Inferred lane (Riot team position); null outside a 5v5 Summoner's Rift game. */
  position: string | null
  isBot: boolean
  /** The player whose profile this is. */
  isSubject: boolean
  /** TrueMain holds this account, so its profile can be linked. */
  isTracked: boolean
  /** Solo/Duo; null when unranked or unknown. */
  ranked: ProfileRanked | null
}
