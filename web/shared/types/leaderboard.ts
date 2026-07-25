// Mirrors backend/Api/ReadModels/Truemains/LeaderboardReadModel.cs.
// See #162.

import type { ProfileIdentity } from './profile'

/**
 * Every region slug the leaderboard exposes, as a runtime list.
 *
 * This is the single source of truth: {@link RegionSlug} is *derived* from it
 * rather than declared beside it, so the two cannot drift. That matters because
 * the runtime copies are validation guards — they narrow untrusted input (query
 * strings, `localStorage`) against this list, and a slug present in the type but
 * missing from the list is silently dropped to `null` instead of failing loudly.
 *
 * JP1 and SEA shards are out of scope until there's demand. Adding a region
 * touches more than this file — the display pills in `LeaderboardFilters.vue`
 * and `home/TruemainsPanel.vue`, the flag artwork in `LeaderboardRegionFlag.vue`,
 * and the platform mapping in `shared/utils/region.ts` all carry their own
 * per-region entries and must be extended too.
 */
export const REGION_SLUGS = ['europe', 'americas', 'korea'] as const

export type RegionSlug = typeof REGION_SLUGS[number]

export interface LeaderboardResponse {
  rows: LeaderboardRowResponse[]
  /** 1-indexed current page. */
  page: number
  /** Rows per page (the value the service clamped to, not the requested one). */
  pageSize: number
  /** Total rows across all pages for the active filter — drives the page-count UI. */
  total: number
}

export interface LeaderboardRowResponse {
  /** 1-based position on the filtered leaderboard, computed server-side. */
  rank: number
  identity: ProfileIdentity
  region: RegionSlug
  /** Null when the account has no rank snapshot yet — never happens in V1 since unranked accounts are excluded. */
  ranked: LeaderboardRanked | null
  stats: LeaderboardStats
  /** Up to 3 most-played champions, descending by play rate. Empty when no main-champion analysis has run. */
  topChampions: LeaderboardTopChampion[]
  /** Primary + secondary lane from position share across the player's mains. Null when no main-champion analysis has run. */
  positions: LeaderboardPositions | null
}

export interface LeaderboardPositions {
  /** Highest-share lane, Riot uppercase (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY). */
  primary: string
  /** Next lane when its share is meaningful (≥ the share floor, below the primary), otherwise null. */
  secondary: string | null
}

export interface LeaderboardRanked {
  tier: string
  division: string
  leaguePoints: number
  /** Sort key used by the backend; exposed so the UI can show the tiebreaker. */
  score: number
}

export interface LeaderboardStats {
  games: number
  wins: number | null
  losses: number | null
  /** `wins / (wins + losses)` when both are known, otherwise null. */
  winRate: number | null
  /** `(kills + assists) / max(1, deaths)` across attributed participant rows, null when none. */
  kda: number | null
}

export interface LeaderboardTopChampion {
  championId: number
  games: number
  /** Player's authoritative play rate for this champion (0..1), from main-champion stats. */
  playRate: number
  /** True when the player is a one-trick pony on this champion (play rate ≥ the OTP threshold), as flagged by main analysis. */
  isOtp: boolean
  /** Player's most-played build on this champion — null when no aggregated build exists yet. */
  primaryKeystoneId: number | null
  secondaryStyleId: number | null
  firstItemId: number | null
}
