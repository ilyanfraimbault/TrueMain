// Mirrors backend/Api/ReadModels/Truemains/MatchSummaryReadModel.cs.
// Team objective counts are intentionally absent; they require ingestion
// changes (see #159) and will be added once those land.

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
  /** Inventory slots 0..5 (length 6). The trinket is in `trinketItemId`. */
  items: number[]
  trinketItemId: number
  /** Riot's role-bound slot — a bot laner's quest boots, other roles' quest reward. 0 when empty. */
  roleBoundItemId: number
  teamId: number
  /** Riot team position (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY). Null when Riot did not assign one. */
  position: string | null
  win: boolean
  /** Null when the rank snapshots around the game window are missing or span a tier/division transition. */
  lpDelta: number | null
  /**
   * TrueMain's per-match performance score, 0–100 — the same scorer, on the same
   * inputs, as the expanded detail panel. See docs/performance-score.md.
   */
  performanceScore: number
  /** 1-based rank of that score among the match's 10 participants. */
  placement: number
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
