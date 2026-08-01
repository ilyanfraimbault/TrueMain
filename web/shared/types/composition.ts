import type {
  BuildItemPath,
  BuildItemSet,
  BuildRunePage,
  BuildSkillOrder,
  BuildSummonerSpells,
  BuildTreeNode,
} from './champions'
import type { MatchSummaryResponse } from './matches'

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
  truemainGameCount: number
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
  /** True when the draft pinned the role opponent (hard requirement). */
  matchupRequested: boolean
  /**
   * False only when the role opponent was requested and no recorded game has
   * that matchup — the client then falls back to the champion's baseline build.
   */
  matchupFound: boolean
  confidence: CompositionConfidence
  build: CompositionBuildRecommendation
}

/**
 * Response of `POST /champions/{id}/composition-build/games` (#940): the
 * games the recommendation for that same draft was computed from, one page at
 * a time, in the selection's own order (mains first, then similarity, recency
 * breaking ties).
 */
export interface CompositionBuildGamesResponse {
  championId: number
  position: string
  patch: string | null
  page: number
  pageSize: number
  /** Selected games across all pages — the recommendation's sample. */
  total: number
  /** Denominator of each game's `score`. */
  maxPossibleScore: number
  games: CompositionGame[]
}

export interface CompositionGamePilot {
  gameName: string
  tagLine: string | null
  /** Riot profile icon id, 0 when never resolved. */
  profileIconId: number
}

export interface CompositionGame {
  /** Similarity score of the game, out of `maxPossibleScore`. */
  score: number
  /** True when the pilot is an active main of the champion. */
  isTruemain: boolean
  /** Null when the game's participant carries no resolved Riot account. */
  pilot: CompositionGamePilot | null
  /** The pilot's slice of the game, in the match-feed row shape. */
  match: MatchSummaryResponse
}
