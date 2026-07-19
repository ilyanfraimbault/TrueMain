// Mirrors backend/Api/ReadModels/Truemains/SearchReadModel.cs.
// See #520.

import type { ProfileIdentity } from './profile'
import type { LeaderboardPositions, RegionSlug } from './leaderboard'

export interface SearchResponse {
  results: SearchResult[]
}

export interface SearchResult {
  identity: ProfileIdentity
  /**
   * One of `europe`, `americas`, `korea` — same slug the leaderboard uses.
   * Typed as a known slug rather than `string`: the search population is
   * filtered to the exposed regions, so the backend's `?? ''` fallback for an
   * unknown platform can't actually be reached here.
   */
  region: RegionSlug
  /** Latest tier/division/LP, or null when the account has no rank snapshot yet. */
  ranked: SearchRanked | null
  /**
   * Up to 3 most-played champion ids (descending play rate) — the same slice
   * the leaderboard row shows. Empty when no main-champion analysis has run.
   */
  topChampionIds: number[]
  /** Primary + secondary lane from position share across the player's mains, or null when unanalysed. */
  positions: LeaderboardPositions | null
}

export interface SearchRanked {
  tier: string
  division: string
  leaguePoints: number
}
