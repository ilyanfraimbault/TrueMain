import { describe, expect, it } from 'vitest'
import {
  DEFAULT_DIRECTORY_ORDER,
  directoryOrderToQuery,
  directoryOrderToSorting,
  leaderboardSortToSorting,
  parseDirectoryOrder,
  sortingToLeaderboardSort,
} from '~/utils/table-sorting'

describe('leaderboard sorting', () => {
  it('draws either server order as its column, descending', () => {
    expect(leaderboardSortToSorting('rank')).toEqual([{ id: 'rank', desc: true }])
    expect(leaderboardSortToSorting('dedication')).toEqual([{ id: 'dedication', desc: true }])
  })

  it('reads the score column as dedication and anything else as rank', () => {
    expect(sortingToLeaderboardSort([{ id: 'dedication', desc: true }])).toBe('dedication')
    // The board has no ascending order: a direction never changes the answer.
    expect(sortingToLeaderboardSort([{ id: 'dedication', desc: false }])).toBe('dedication')
    expect(sortingToLeaderboardSort([{ id: 'games', desc: true }])).toBe('rank')
    expect(sortingToLeaderboardSort([])).toBe('rank')
    expect(sortingToLeaderboardSort(undefined)).toBe('rank')
  })
})

describe('directory order', () => {
  it('reads a known column and direction from the URL', () => {
    expect(parseDirectoryOrder('winRate', 'asc')).toEqual({ sort: 'winRate', order: 'asc' })
    expect(parseDirectoryOrder('tier', 'desc')).toEqual({ sort: 'tier', order: 'desc' })
  })

  it('falls back to most picked, descending, on anything it does not know', () => {
    expect(parseDirectoryOrder(undefined, undefined)).toEqual(DEFAULT_DIRECTORY_ORDER)
    expect(parseDirectoryOrder('name', 'sideways')).toEqual({ sort: 'pickRate', order: 'desc' })
    expect(parseDirectoryOrder(['games'], 'asc')).toEqual({ sort: 'pickRate', order: 'asc' })
  })

  it('maps an order to the table state', () => {
    expect(directoryOrderToSorting({ sort: 'banRate', order: 'asc' })).toEqual([{ id: 'banRate', desc: false }])
    expect(directoryOrderToSorting({ sort: 'games', order: 'desc' })).toEqual([{ id: 'games', desc: true }])
  })

  it('leaves the defaults out of the URL so the resting page stays bare', () => {
    expect(directoryOrderToQuery(DEFAULT_DIRECTORY_ORDER)).toEqual({ sort: null, order: null })
    expect(directoryOrderToQuery({ sort: 'pickRate', order: 'asc' })).toEqual({ sort: null, order: 'asc' })
    expect(directoryOrderToQuery({ sort: 'tier', order: 'desc' })).toEqual({ sort: 'tier', order: null })
  })
})
