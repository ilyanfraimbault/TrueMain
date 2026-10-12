import { describe, expect, it } from 'vitest'
import { activityFromGames } from '~/utils/activity-from-games'
import { DAY, MINUTE, game } from './fixtures'

/**
 * The dashboard's activity grid, folded from the client's games instead of read
 * from TrueMain. It has to land on the same UTC days as `/activity` — a game at
 * 23:59 UTC belongs to that day, whatever the player's clock says — keep an empty
 * day empty (no rate, never 0%), count no remake, and leave the patch window
 * empty since the client's games carry no patch.
 */

// Noon UTC on 8 October 2026.
const TODAY = Date.UTC(2026, 9, 8)
const NOW = TODAY + 12 * 60 * MINUTE

describe('activityFromGames', () => {
  it('folds the week and the month on UTC days, one cell per day, played or not', () => {
    const { week, month } = activityFromGames([
      game({ gameId: 1, playedAt: TODAY + MINUTE, win: true }),
      game({ gameId: 2, playedAt: TODAY - 1, win: false }), // the last millisecond of yesterday, UTC
      game({ gameId: 3, playedAt: TODAY - 6 * DAY, win: true }),
      game({ gameId: 4, playedAt: TODAY - 7 * DAY, win: true }), // out of the week, in the month
    ], NOW)

    expect(week.buckets.map(bucket => bucket.key)).toEqual([
      '2026-10-02', '2026-10-03', '2026-10-04', '2026-10-05', '2026-10-06', '2026-10-07', '2026-10-08',
    ])
    expect(week.buckets[0]).toMatchObject({ games: 1, wins: 1, winRate: 1 })
    expect(week.buckets[1]).toMatchObject({ games: 0, wins: 0, winRate: null })
    expect(week.buckets[5]).toMatchObject({ key: '2026-10-07', games: 1, wins: 0, winRate: 0 })
    expect(week.buckets[6]).toMatchObject({ key: '2026-10-08', startUtc: '2026-10-08T00:00:00.000Z', games: 1, wins: 1 })
    expect(week).toMatchObject({ games: 3, wins: 2, patch: null })

    expect(month.buckets).toHaveLength(30)
    expect(month).toMatchObject({
      games: 4,
      wins: 3,
      winRate: 0.75,
      coverageFromUtc: '2026-09-09T00:00:00.000Z',
      coverageToUtc: '2026-10-08T00:00:00.000Z',
    })
  })

  it("lists today's games one cell each, oldest first, with the champion played", () => {
    const { day } = activityFromGames([
      game({ gameId: 2, playedAt: TODAY + 120 * MINUTE, win: false, championId: 103 }),
      game({ gameId: 1, playedAt: TODAY + 60 * MINUTE, win: true, championId: 157 }),
      game({ gameId: 3, playedAt: TODAY - 60 * MINUTE }), // yesterday
    ], NOW)

    expect(day.buckets.map(bucket => [bucket.key, bucket.games, bucket.wins, bucket.championId])).toEqual([
      ['1', 1, 1, 157],
      ['2', 1, 0, 103],
    ])
    expect(day).toMatchObject({ games: 2, wins: 1, winRate: 0.5 })
  })

  it('counts no remake', () => {
    const { day, week, month } = activityFromGames([game({ playedAt: TODAY + MINUTE, remake: true })], NOW)
    expect(day.buckets).toEqual([])
    expect(week).toMatchObject({ games: 0, winRate: null })
    expect(month.games).toBe(0)
  })

  it('leaves the patch window empty: the client\'s games carry no patch', () => {
    const { patch } = activityFromGames([game({ playedAt: TODAY + MINUTE })], NOW)
    expect(patch).toMatchObject({ mode: 'patch', patch: null, buckets: [], games: 0, winRate: null, coverageFromUtc: null })
  })
})
