import type {
  BuildItemPath,
  BuildItemSet,
  BuildRunePage,
  BuildSkillOrder,
  BuildSummonerSpells,
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
 * Win-weighted build aggregated from the most similar games. Every dimension
 * is nullable: sparse data drops the dimension instead of fabricating one.
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
}

export interface CompositionBuildResponse {
  championId: number
  position: string
  patch: string | null
  eloBracket: string
  confidence: CompositionConfidence
  build: CompositionBuildRecommendation
}
