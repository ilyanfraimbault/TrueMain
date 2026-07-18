import type {
  BuildItemPath,
  BuildItemSet,
  BuildRunePage,
  BuildSkillOrder,
  BuildSummonerSpells,
  BuildTreeNode,
} from './champions'

/** One known pick of the draft: a champion at a position. */
export interface CompositionSlotInput {
  championId: number
  position: string
}

/** Request body of `POST /champions/{id}/composition-build`. */
export interface CompositionBuildRequest {
  position: string
  patch?: string
  eloBracket?: string
  allies: CompositionSlotInput[]
  enemies: CompositionSlotInput[]
}

/**
 * Confidence signals of one recommendation: how much data backs it and how
 * close the sample got to the requested draft (0 when no slot was provided).
 */
export interface CompositionConfidence {
  sampleSize: number
  candidatePoolSize: number
  maxPossibleScore: number
  meanSimilarity: number
}

/**
 * Similarity-and-win-weighted build aggregated from the most similar games.
 * Every dimension is nullable: sparse data drops the dimension instead of
 * fabricating one.
 */
export interface CompositionBuildRecommendation {
  gamesConsidered: number
  wins: number
  runePage: BuildRunePage | null
  starterItems: BuildItemSet | null
  boots: BuildItemSet | null
  corePath: BuildItemPath | null
  situationalItems: BuildItemSet[]
  summonerSpells: BuildSummonerSpells | null
  skillOrder: BuildSkillOrder | null
  /** First item of `corePath` (the build-tree root), 0 when unresolved. */
  firstItemId: number
  /** Item-progression tree of the sampled games opening with `firstItemId`. */
  buildTree: BuildTreeNode[]
}

export interface CompositionBuildResponse {
  championId: number
  position: string
  patch: string | null
  eloBracket: string
  /** True when the draft pinned the lane opponent (hard requirement). */
  matchupRequested: boolean
  /**
   * False only when the lane opponent was requested and no recorded game has
   * that matchup — the client then falls back to the champion's baseline build.
   */
  matchupFound: boolean
  confidence: CompositionConfidence
  build: CompositionBuildRecommendation
}
