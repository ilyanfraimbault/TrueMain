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
  /**
   * How the lane went in the sampled games (#1117) — measured over exactly the
   * games `confidence` counts, so the tool's stat line describes one population
   * throughout. It used to read the matchup aggregate, whose champion side is
   * mains-only (#1087), which showed "—" beside a sample of eight games whenever
   * no main had played the matchup.
   */
  lane: CompositionLane
  build: CompositionBuildRecommendation
}

/** The lane at 15 minutes over a recommendation's own sample. */
export interface CompositionLane {
  /**
   * Sampled games where both lane sides had a 15-minute reading — smaller than
   * the sample, since a game that ended early is a game but not a judgeable lane.
   * The denominator of both gaps below, evens included.
   */
  measuredGames: number
  /** Of those, the ones settled past the gold threshold either way. */
  decidedGames: number
  /** Share of `decidedGames` won. `null` when none were — never render it as 0%. */
  winRate: number | null
  /** Mean gold gap at 15 min over `measuredGames`, signed. `null` when unmeasured. */
  averageGoldDiffAt15: number | null
  /**
   * Mean XP gap over the same games. Beside the gold, not derived from it: a lane
   * won on kills and lost on waves shows one ahead and the other behind.
   */
  averageXpDiffAt15: number | null
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
