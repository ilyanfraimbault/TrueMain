// Mirrors backend/Api/ReadModels/Truemains/SearchReadModel.cs.
// See #520.

import type { ProfileIdentity } from './profile'
import type { RegionSlug } from './leaderboard'

export interface SearchResponse {
  results: SearchResult[]
}

export interface SearchResult {
  identity: ProfileIdentity
  /** One of `europe`, `americas`, `korea` — same slug the leaderboard uses. The search population is exposed-region only, so it's always a known slug. */
  region: RegionSlug
  /** Latest tier/division/LP, or null when the account has no rank snapshot yet. */
  ranked: SearchRanked | null
}

export interface SearchRanked {
  tier: string
  division: string
  leaguePoints: number
}
