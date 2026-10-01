import type { LeaderboardSort } from '~~/shared/types/leaderboard'
import type { ChampionDirectorySort, SortOrder } from '~~/shared/types/champion-directory'
import { CHAMPION_DIRECTORY_SORTS } from '~~/shared/types/champion-directory'

/**
 * The list tables' sort state (#1734), between the URL — the source of truth,
 * so a sorted page is a link — and `UTable`'s TanStack `sorting` model. The
 * tables sort *manually*: a header click changes the URL, the URL changes the
 * API request, and the rows that come back are already in order.
 *
 * Structural rather than imported from `@tanstack/vue-table`, which reaches
 * this app only through Nuxt UI.
 */
export type TableSorting = { id: string, desc: boolean }[]

/** The leaderboard has two server orders, both descending: column ids are the API's `sort` values. */
export function leaderboardSortToSorting(sort: LeaderboardSort): TableSorting {
  return [{ id: sort, desc: true }]
}

/** Any column but the score (or an emptied state) is the default rank order. */
export function sortingToLeaderboardSort(sorting: TableSorting | undefined): LeaderboardSort {
  return sorting?.[0]?.id === 'dedication' ? 'dedication' : 'rank'
}

export interface DirectoryOrder {
  sort: ChampionDirectorySort
  order: SortOrder
}

/** Most picked first — what the API returns without a `sort`. */
export const DEFAULT_DIRECTORY_ORDER: DirectoryOrder = { sort: 'pickRate', order: 'desc' }

const DIRECTORY_SORTS = new Set<string>(CHAMPION_DIRECTORY_SORTS)

/** `?sort=` / `?order=` read tolerantly: junk falls back to the default, as the API does. */
export function parseDirectoryOrder(rawSort: unknown, rawOrder: unknown): DirectoryOrder {
  const sort = typeof rawSort === 'string' && DIRECTORY_SORTS.has(rawSort)
    ? rawSort as ChampionDirectorySort
    : DEFAULT_DIRECTORY_ORDER.sort
  return { sort, order: rawOrder === 'asc' ? 'asc' : 'desc' }
}

export function directoryOrderToSorting({ sort, order }: DirectoryOrder): TableSorting {
  return [{ id: sort, desc: order === 'desc' }]
}

/** The URL params for an order; null for the default, so the resting URL stays bare. */
export function directoryOrderToQuery({ sort, order }: DirectoryOrder): { sort: string | null, order: string | null } {
  return {
    sort: sort === DEFAULT_DIRECTORY_ORDER.sort ? null : sort,
    order: order === DEFAULT_DIRECTORY_ORDER.order ? null : order,
  }
}
