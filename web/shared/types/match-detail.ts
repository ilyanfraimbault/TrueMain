// Mirrors backend/Api/ReadModels/Truemains/MatchDetailReadModel.cs.
// Single-match detail payload for GET /truemains/{nameTag}/matches/{matchId}.
// Per issue #523 this carries no team objectives and no ward counts — only data
// the DB already has. Derived per-minute rates, laning diffs and the performance
// score / placement / MVP / ACE accolades (#639) are computed server-side.

export interface MatchDetailResponse {
  matchId: string
  queueId: number
  gameMode: string
  gameStartTimeUtc: string
  gameDurationSeconds: number
  gameVersion: string
  participants: MatchDetailParticipant[]
}

export interface MatchDetailParticipant {
  participantId: number
  championId: number
  champLevel: number
  summonerName: string
  /** Riot game name when the participant is a tracked account, else null. */
  gameName: string | null
  /** Riot tag line when the participant is a tracked account, else null. */
  tagLine: string | null
  /** 100 = blue side, 200 = red side. */
  teamId: number
  /** Riot team position (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY); empty when unknown. */
  teamPosition: string
  win: boolean
  kills: number
  deaths: number
  assists: number
  /** Inventory slots 0..6 (length 7). The trinket is in `trinketItemId`. */
  items: number[]
  trinketItemId: number
  summoner1Id: number
  summoner2Id: number
  primaryStyleId: number
  subStyleId: number
  /** Keystone perk id (slot 0 of the primary tree); 0 when the page failed to ingest. */
  keystoneId: number
  totalDamageDealtToChampions: number
  visionScore: number
  goldEarned: number
  /** Sum of lane minions + neutral monsters. */
  cs: number
  /** Approximate rank tier at game time (closest snapshot). Null when none exists. */
  rank: MatchDetailRank | null

  // Derived (computed server-side)
  /** Kill participation 0..1. */
  killParticipation: number
  csPerMin: number
  damagePerMin: number
  goldPerMin: number
  visionPerMin: number
  /**
   * TrueMain performance score, 0–100 — role-aware weighted blend of KDA, kill
   * participation, damage share, gold share, CS/min, vision/min and the @15
   * laning leads. Model + weights: backend/Core/Lol/Performance/PerformanceScore.cs.
   */
  performanceScore: number
  /** 1-based rank of this participant's score within the match (1 = best of 10). */
  placement: number
  /** Top-scoring participant of the winning side. */
  isMvp: boolean
  /** Top-scoring participant of the losing side. */
  isAce: boolean
  /** Laning diffs @15 vs the opposing TeamPosition. Null when either side lacks a @15 snapshot. */
  laning15: MatchDetailLaning15 | null
  /** True when this participant reached level 2 before their lane opponent; null when no opponent / missing data. */
  firstToLevelTwo: boolean | null

  /** Full rune page (6 selections) in catalog order. */
  runes: MatchDetailRune[]
  statPerkOffense: number
  statPerkFlex: number
  statPerkDefense: number

  /** Build order (purchases / sells / undos) in chronological order. */
  itemEvents: MatchDetailItemEvent[]
  /** Skill order (Q/W/E/R level-ups) in chronological order. */
  skillEvents: MatchDetailSkillEvent[]

  /**
   * Measured jungle first clear (#1188). Null for non-junglers and for matches
   * ingested without timeline coverage.
   */
  jungleClear: MatchDetailJungleClear | null
}

/**
 * Carries no camp order on purpose: Riot samples positions once a minute while a
 * clear takes ~1:45, so the camp sequence is not reconstructable (#1188).
 */
export interface MatchDetailJungleClear {
  /** Camp the jungler opened on (JungleCamp enum name); null when unknown. */
  startCamp: string | null
  /** Per-minute clear-speed samples, ascending by timestamp. */
  samples: MatchDetailJungleClearSample[]
  /** First frame (ms) where jungle CS reached a full clear's worth; null if never. */
  fullClearTimeMs: number | null
  /** Camps in a full first clear (6) — the denominator for a sample's campsCleared. */
  fullClearCamps: number
}

export interface MatchDetailJungleClearSample {
  timestampMs: number
  /** Camps fully cleared by this frame. Exact: League scores a camp as 4 CS whatever its monster count. */
  campsCleared: number
  /** Raw cumulative jungle CS — a value between two multiples of 4 is a camp mid-clear. */
  jungleCs: number
  /** Sampled map position — where the jungler was, not a camp claim. */
  x: number
  y: number
}

export interface MatchDetailRank {
  tier: string
  division: string
  leaguePoints: number
}

export interface MatchDetailLaning15 {
  csDiff: number
  goldDiff: number
  xpDiff: number
}

export interface MatchDetailRune {
  styleId: number
  selectionIndex: number
  perkId: number
}

export interface MatchDetailItemEvent {
  timestampMs: number
  /** ITEM_PURCHASED / ITEM_SOLD / ITEM_DESTROYED / ITEM_UNDO. */
  eventType: string
  itemId: number
  beforeId: number | null
  afterId: number | null
}

export interface MatchDetailSkillEvent {
  timestampMs: number
  /** 1 = Q, 2 = W, 3 = E, 4 = R. */
  skillSlot: number
}
