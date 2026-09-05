import { describe, expect, it } from 'vitest'
import {
  ACTIVITY_EMPTY_FILL,
  ACTIVITY_LEVELS,
  ACTIVITY_RAMP,
  activityBucketLabel,
  activityBucketResult,
  activityCellFill,
  activityCellLevel,
  activityCellsAreGames,
  activityMaxGames,
} from '~/utils/activity-heatmap'
import type { ActivityBucket, ActivitySeries } from '~~/shared/types/activity'

/**
 * The activity heatmap's colour scale and copy (#927, reshaped in #1473).
 *
 * The load-bearing property here is the one the API can only offer, not enforce:
 * a cell with `games: 0` and a cell with `winRate: 0` must not render alike. The
 * fill helper answers `null` for the first and a real ramp step for the second,
 * and the tooltip says "No games" rather than printing an invented 0%.
 *
 * The second property is what the grid is *about*: intensity tracks games
 * played, and nothing else. A losing day the player queued five times on has to
 * come out brighter than a winning day they queued once on — the squares answer
 * "was he playing", the tooltip answers "how did it go".
 *
 * The third is the one #1473 turns on: the *unit* is the day in every window but
 * `day`, so the result-coloured fallback has to be chosen by the window and
 * never inferred from the data.
 */

function bucket(partial: Partial<ActivityBucket> = {}): ActivityBucket {
  return {
    key: '2026-07-29',
    startUtc: '2026-07-29T00:00:00Z',
    games: 4,
    wins: 3,
    winRate: 0.75,
    championId: null,
    ...partial,
  }
}

function series(partial: Partial<ActivitySeries> = {}): ActivitySeries {
  return {
    mode: 'patch',
    patch: '16.6',
    coverageFromUtc: '2026-07-03T00:00:00Z',
    coverageToUtc: '2026-07-29T00:00:00Z',
    buckets: [bucket()],
    games: 4,
    wins: 3,
    winRate: 0.75,
    ...partial,
  }
}

describe('activityCellFill', () => {
  it('gives an empty period no fill at all, so it cannot look like a quiet one', () => {
    expect(activityCellFill(bucket({ games: 0, wins: 0, winRate: null }), 8)).toBeNull()
  })

  it('gives a period that lost everything a real step on the ramp', () => {
    const fill = activityCellFill(bucket({ games: 3, wins: 0, winRate: 0 }), 8)
    expect(fill).not.toBeNull()
    expect(ACTIVITY_RAMP).toContain(fill)
    expect(fill).not.toBe(ACTIVITY_EMPTY_FILL)
  })

  it('paints on volume alone, so two periods of equal size match whatever the result', () => {
    const swept = activityCellFill(bucket({ games: 4, wins: 4, winRate: 1 }), 8)
    const lost = activityCellFill(bucket({ games: 4, wins: 0, winRate: 0 }), 8)
    expect(swept).toBe(lost)
  })

  it('reads a busier period as brighter than a quiet one', () => {
    const thin = activityCellLevel(bucket({ games: 1, wins: 1, winRate: 1 }), 10)
    const busy = activityCellLevel(bucket({ games: 10, wins: 10, winRate: 1 }), 10)
    expect(busy).toBeGreaterThan(thin)
    // …and it beats a winning day the player barely showed up for, which is the
    // whole point of dropping the win rate out of the colour.
    expect(activityCellLevel(bucket({ games: 10, wins: 0, winRate: 0 }), 10))
      .toBeGreaterThan(thin)
  })

  it('keeps every populated cell inside the ramp', () => {
    expect(activityCellLevel(bucket({ games: 1, wins: 0, winRate: 0 }), 100)).toBe(1)
    expect(activityCellLevel(bucket({ games: 100, wins: 50, winRate: 0.5 }), 100)).toBe(ACTIVITY_LEVELS)
    // A bucket busier than the series maximum cannot happen off the wire, but it
    // must clamp rather than index past the end of the ramp.
    expect(activityCellFill(bucket({ games: 400, wins: 200, winRate: 0.5 }), 100))
      .toBe(ACTIVITY_RAMP[ACTIVITY_RAMP.length - 1])
  })

  it('falls back to the result in the per-game window, where volume says nothing', () => {
    // Every cell there holds exactly one game: a flat strip would carry no shape
    // at all, so the won games take the top step and the lost ones sit below.
    const won = activityCellLevel(bucket({ games: 1, wins: 1, winRate: 1 }), 1, true)
    const lost = activityCellLevel(bucket({ games: 1, wins: 0, winRate: 0 }), 1, true)
    expect(won).toBe(ACTIVITY_LEVELS)
    expect(lost).toBeLessThan(won)
    expect(lost).toBeGreaterThan(0)
  })

  it('keeps colouring on volume when a calendar window happens to top out at one game', () => {
    // A patch the player never queued twice in a day on is still made of days.
    // Inferring the per-game fallback from `maxGames <= 1` would start colouring
    // those days by result, which is the read #1452 removed from the grid.
    const won = activityCellLevel(bucket({ games: 1, wins: 1, winRate: 1 }), 1)
    const lost = activityCellLevel(bucket({ games: 1, wins: 0, winRate: 0 }), 1)
    expect(won).toBe(lost)
  })

  it('treats a period with no games as empty however the rate reads', () => {
    // Defensive: the wire contract ties the two together, but a cell with no
    // games is an idle period whatever else the payload says.
    expect(activityCellFill(bucket({ games: 0, wins: 0, winRate: 0 }), 8)).toBeNull()
  })
})

describe('activityMaxGames', () => {
  it('reports the busiest cell, ignoring the empty ones', () => {
    expect(activityMaxGames(series({
      buckets: [
        bucket({ games: 0, wins: 0, winRate: null }),
        bucket({ games: 7, wins: 4, winRate: 4 / 7 }),
        bucket({ games: 2, wins: 1, winRate: 0.5 }),
      ],
    }))).toBe(7)
  })

  it('reports 0 for a series with no cells', () => {
    expect(activityMaxGames(series({ buckets: [] }))).toBe(0)
  })
})

describe('activityBucketResult', () => {
  it('says "No games" rather than printing a 0% nobody measured', () => {
    expect(activityBucketResult(bucket({ games: 0, wins: 0, winRate: null }))).toBe('No games')
  })

  it('keeps the wins-over-games shape for a single game too', () => {
    expect(activityBucketResult(bucket({ games: 1, wins: 1, winRate: 1 }))).toBe('1/1 · 100%')
    expect(activityBucketResult(bucket({ games: 1, wins: 0, winRate: 0 }))).toBe('0/1 · 0%')
  })

  it('prints wins over games and the rate for an aggregated period', () => {
    expect(activityBucketResult(bucket({ games: 4, wins: 3, winRate: 0.75 })))
      .toBe('3/4 · 75%')
    // A genuine 0% is printed as such — the fact the empty case must not borrow.
    expect(activityBucketResult(bucket({ games: 3, wins: 0, winRate: 0 })))
      .toBe('0/3 · 0%')
  })
})

describe('activityCellsAreGames', () => {
  it('is true for the day window only', () => {
    expect(activityCellsAreGames('day')).toBe(true)
    expect(activityCellsAreGames('week')).toBe(false)
    expect(activityCellsAreGames('patch')).toBe(false)
  })
})

describe('activityBucketLabel', () => {
  it('labels a calendar cell with the UTC day, not the viewer local one', () => {
    // 23:30Z belongs to the 29th; a local-timezone format would drift it.
    expect(activityBucketLabel(bucket({ startUtc: '2026-07-29T23:30:00Z' }), 'patch'))
      .toBe('Wed, Jul 29')
    expect(activityBucketLabel(bucket({ startUtc: '2026-07-27T00:00:00Z' }), 'week'))
      .toBe('Mon, Jul 27')
  })

  it('labels a game cell down to the minute, since a day holds several', () => {
    const label = activityBucketLabel(bucket({ startUtc: '2026-07-29T18:05:00Z' }), 'day')
    expect(label).toContain('Jul 29')
    expect(label).toContain('6:05')
  })
})
