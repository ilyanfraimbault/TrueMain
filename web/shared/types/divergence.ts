/**
 * Canonical `dimension` values returned by the divergence endpoint. The card
 * switches its copy and its icon rendering on these, so they mirror
 * `BuildDivergenceDimensions` on the backend exactly.
 */
export const DIVERGENCE_DIMENSIONS = ['starterItems', 'boots', 'itemPath', 'skillOrder'] as const

export type DivergenceDimension = typeof DIVERGENCE_DIMENSIONS[number]

/**
 * "<player> vs mains" payload
 * (`GET /api/truemains/{nameTag}/champions/{championId}/divergence`): how one
 * player's habits on a champion compare to what the champion's other mains do
 * at the same patch and position.
 *
 * Every number here comes from the aggregates behind the champion page — the
 * card renders them, it never derives a metric of its own.
 */
export interface PlayerBuildDivergenceResponse {
  championId: number
  /** Patch both sides were computed for (resolved from the player's slice). */
  patch: string
  /** Position both sides were computed for (the player's dominant lane by default). */
  position: string
  /** Games the player has in the slice. */
  playerGames: number
  /** Games the reference pool has in the same slice, the player's own excluded. */
  mainsGames: number
  /** Distinct accounts behind `mainsGames`. */
  mainsPlayers: number
  /** Games the player needs before the comparison is drawn. */
  minPlayerGames: number
  /** Games the reference pool needs before it is worth comparing to. */
  minMainsGames: number
  /** `playerGames` cleared `minPlayerGames`. */
  minSampleMet: boolean
  /** `mainsGames` cleared `minMainsGames`. */
  referenceSampleMet: boolean
  /**
   * One row per compared dimension, most actionable first (diverging rows
   * before matching ones, then by how strongly the mains agree). Empty when
   * either sample floor is missed.
   */
  dimensions: BuildDivergence[]
}

export interface BuildDivergence {
  dimension: DivergenceDimension
  /** The two dominant choices differ. Matching rows are returned too. */
  diverges: boolean
  player: BuildChoice
  mains: BuildChoice
  /** Mains' games that made the *player's* choice. */
  mainsGamesOnPlayerChoice: number
  /** `mainsGamesOnPlayerChoice` over the mains' total games in the slice. */
  mainsRateOnPlayerChoice: number
  /** Win rate the mains post on the player's choice; null when no mains game made it. */
  mainsWinRateOnPlayerChoice: number | null
}

/**
 * One dominant choice inside a pool. `itemIds` carries the item dimensions
 * (starter set, the single boots id, or the completed path in order) and
 * `skills` the skill-order one — exactly one of the two is ever populated.
 */
export interface BuildChoice {
  itemIds: number[]
  skills: string[]
  games: number
  /** `games` over the pool's total games in the slice. */
  pickRate: number
  winRate: number
}
