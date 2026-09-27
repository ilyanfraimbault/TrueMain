/** Mirrors `DraftEnemyLaneReadModel` on the API. */
export interface EnemyLane {
  championId: number
  position: string
  /** 0..1. Half is a coin flip between two readings, not "half right". */
  confidence: number
  pinned: boolean
}

/** Mirrors `DraftCandidateReadModel`. */
export interface DraftCandidate {
  championId: number
  matchupDelta: number
  matchupGames: number
  synergyDelta: number
  synergyGames: number
  score: number
  thinSample: boolean
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

/** Which champions the draft endpoint is asked to rank: the lane's meta picks, or the ones below them. */
export type DraftPool = 'meta' | 'offmeta'

/** One slot of a team, as the draft screen hands it over. */
export interface TeamRow {
  championId: number | null
  lane: Lane | null
  /** A pick locked in; false while it is only hovered. */
  locked: boolean
  me?: boolean
  /** The guesser's certainty about an enemy's lane, 0..1. */
  confidence?: number
  /** An enemy lane the player set by hand. */
  pinned?: boolean
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
