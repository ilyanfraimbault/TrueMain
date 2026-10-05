/** Mirrors `DraftEnemyLaneReadModel` on the API. */
export interface EnemyLane {
  championId: number
  position: string
  /** 0..1. Half is a coin flip between two readings, not "half right". */
  confidence: number
  pinned: boolean
}

/** Mirrors `DraftReasonKinds` on the API. */
export type DraftReasonKind =
  | 'laneMatchup'
  | 'lanePhase'
  | 'blindSafety'
  | 'laneStrength'
  | 'synergy'
  | 'laneThreat'
  | 'banRate'

/**
 * Mirrors `DraftReasonReadModel`: one measured fact behind a suggestion, as
 * data. The client words it (`utils/draft-reasons.ts`); every number in the
 * sentence is one of these fields.
 */
export interface DraftReason {
  kind: DraftReasonKind | string
  championId: number | null
  position: string | null
  /** Win-rate difference in probability points (0.031 = +3.1 pts). */
  delta: number | null
  games: number
  /** A plain rate (0..1): a lane-phase win rate, a ban rate. */
  rate: number | null
  /** The solver's probability that an enemy is our lane opponent — a lane guess, never a chance to win. */
  probability: number | null
  /** Share of the lane's games a champion is played in. */
  share: number | null
  count: number | null
  of: number | null
  /** Rests on an ally's hover rather than a lock. */
  tentative: boolean
}

/** Mirrors `DraftBlindReadModel`. */
export interface DraftBlind {
  delta: number
  games: number
  losingInto: number
  likelyOpponents: number
}

/** Mirrors `DraftCandidateReadModel`. */
export interface DraftCandidate {
  championId: number
  matchupDelta: number
  matchupGames: number
  blind?: DraftBlind
  strengthDelta?: number
  strengthGames?: number
  synergyDelta: number
  synergyGames: number
  /** A ranking key, never displayed: not a chance to win. */
  score: number
  thinSample: boolean
  /** The facts that carry the pick, strongest first. Absent from answers older than #1906. */
  reasons?: DraftReason[]
  patch?: string | null
}

/** Mirrors `DraftBanCandidateReadModel`. */
export interface DraftBanCandidate {
  championId: number
  /** A ranking key, never displayed. */
  score: number
  reasons: DraftReason[]
}

/** One card of the suggestion strip: a pick or a ban, and the facts behind it. */
export interface DraftSuggestionItem {
  championId: number
  reasons: DraftReason[]
  /** Few games behind every reason. */
  thin: boolean
}

/** Mirrors `DraftBanResponse`. */
export interface DraftBans {
  position: string
  patch: string | null
  eloBracket: string
  /** `pick` (our declared pick), `pool` (our mastery pool) or `none` (most banned on the lane). */
  target: 'pick' | 'pool' | 'none' | string
  targetChampionIds: number[]
  candidates: DraftBanCandidate[]
}

/** Mirrors `DraftRecommendationResponse`. */
export interface DraftRecommendation {
  position: string
  patch: string | null
  eloBracket: string
  enemyLanes: EnemyLane[]
  laneOpponentChampionId: number | null
  laneOpponentConfidence: number
  candidates: DraftCandidate[]
}

/** One slot of a team, as the draft screen hands it over. */
export interface TeamRow {
  championId: number | null
  lane: Lane | null
  /** A pick locked in; false while it is only hovered. */
  locked: boolean
  me?: boolean
}

export const LANES = ['TOP', 'JUNGLE', 'MIDDLE', 'BOTTOM', 'UTILITY'] as const
export type Lane = (typeof LANES)[number]

export const LANE_LABELS: Record<Lane, string> = {
  TOP: 'Top',
  JUNGLE: 'Jungle',
  MIDDLE: 'Mid',
  BOTTOM: 'Bot',
  UTILITY: 'Support',
}

/** Riot's own lane icons, copied from the site's `public/positions`. */
export function laneIconUrl(lane: string): string {
  return `positions/icon-position-${lane.toLowerCase()}.png`
}
