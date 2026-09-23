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
