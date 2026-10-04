import { LEADERBOARD_SORTS, type LeaderboardSort } from '../types/leaderboard'

const SORTS = new Set<string>(LEADERBOARD_SORTS)

/** `?sort=` read tolerantly: junk (or a repeated param) falls back to the default `rank`, as the API does. */
export function parseLeaderboardSort(raw: unknown): LeaderboardSort {
  return typeof raw === 'string' && SORTS.has(raw) ? raw as LeaderboardSort : 'rank'
}
