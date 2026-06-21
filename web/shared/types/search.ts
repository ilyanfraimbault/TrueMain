// Mirrors backend/Api/ReadModels/Truemains/SearchReadModel.cs.
// See #520.

import type { ProfileIdentity } from './profile'
import type { RegionSlug } from './leaderboard'

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
}

export interface SearchRanked {
  tier: string
  division: string
  leaguePoints: number
}
