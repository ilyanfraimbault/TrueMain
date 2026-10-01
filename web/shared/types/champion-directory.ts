import type { ChampionSummaryResponse } from './champions'

/**
 * `GET /champions/directory` (#1734): one page of the champion directory,
 * narrowed, ordered and paged by the API. Mirrors
 * `ChampionDirectoryPageReadModel` / `ChampionDirectoryQuery` on the backend.
 */

/** The columns the directory can be ordered by; the API falls back to `pickRate` on anything else. */
export const CHAMPION_DIRECTORY_SORTS = ['pickRate', 'winRate', 'banRate', 'games', 'tier'] as const
export type ChampionDirectorySort = typeof CHAMPION_DIRECTORY_SORTS[number]

/** `desc` is strongest first; the API reads anything but `asc` as `desc`. */
export type SortOrder = 'asc' | 'desc'

export interface ChampionDirectoryResponse {
  rows: ChampionSummaryResponse[]
  page: number
  pageSize: number
  /** Lines the filters keep, across every page. */
  total: number
  /** The resolved patch — present even when `rows` is empty. */
  patchVersion: string
}
