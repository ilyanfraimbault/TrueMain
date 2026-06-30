// Mirrors backend/Api/ReadModels/Truemains/MatchDetailReadModel.cs.
// Single-match detail payload for GET /truemains/{nameTag}/matches/{matchId}.
// Per issue #523 this carries no team objectives, no performance/MVP/ACE score
// and no ward counts — only data the DB already has. Derived per-minute rates
// and laning diffs are computed server-side.

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
