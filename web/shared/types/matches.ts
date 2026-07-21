// Mirrors backend/Api/ReadModels/Truemains/MatchSummaryReadModel.cs.
// Damage, vision score, performance score and team objective counts are
// intentionally absent; they require ingestion changes (see #159) and will
// be added once those land.

export interface MatchSummaryResponse {
  matchId: string
  queueId: number
  gameMode: string
  gameStartTimeUtc: string
  gameDurationSeconds: number
  self: MatchSummarySelf
  participants: MatchSummaryParticipant[]
}

export interface MatchSummarySelf {
  championId: number
  championLevel: number
  summoner1Id: number
  summoner2Id: number
  primaryStyleId: number
  subStyleId: number
  keystoneId: number
  kills: number
  deaths: number
  assists: number
  cs: number
  killParticipation: number
  items: number[]
  trinketItemId: number
  teamId: number
  /** Riot team position (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY). Null when Riot did not assign one. */
  position: string | null
  win: boolean
  /** Null when the rank snapshots around the game window are missing or span a tier/division transition. */
  lpDelta: number | null
  isMvp: boolean
  isAce: boolean
}

export interface MatchSummaryParticipant {
  championId: number
  teamId: number
  /** Riot team position (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY). Null when Riot did not assign one. */
  position: string | null
  gameName: string | null
  tagLine: string | null
}

export interface MatchSummariesResponse {
  matches: MatchSummaryResponse[]
  /** 1-indexed current page. */
  page: number
  /** Page size the server actually used (after clamping). */
  pageSize: number
  /** Total matches available for the player across all pages. */
  total: number
}
