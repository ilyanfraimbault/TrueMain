import { describe, expect, it } from 'vitest'
import {
  ACTIVITY_LOSS_RGB,
  ACTIVITY_MAX_ALPHA,
  ACTIVITY_MIN_ALPHA,
  ACTIVITY_WIN_RGB,
  activityBucketLabel,
  activityBucketResult,
  activityCellFill,
  activityCoverageNote,
  activityMaxGames,
} from '~/utils/activity-heatmap'
import type { ActivityBucket, ActivitySeries } from '~~/shared/types/activity'

/**
 * The activity heatmap's colour scale and copy (#927).
 *
 * The load-bearing property here is the one the API can only offer, not enforce:
 * a cell with `games: 0` and a cell with `winRate: 0` must not render alike. The
 * fill helper answers `null` for the first and a rose fill for the second, and
 * the tooltip says "No games" rather than printing an invented 0%.
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
    mode: 'day',
    source: 'matches',
    scope: 'allChampions',
    championId: null,
    retentionBounded: true,
    coverageFromUtc: '2026-07-03T00:00:00Z',
    coverageToUtc: '2026-07-29T00:00:00Z',
    buckets: [bucket()],
    games: 4,
    wins: 3,
    winRate: 0.75,
    ...partial,
  }
}

/** `rgba(r, g, b, a)` → its alpha. */
function alphaOf(fill: string): number {
  const alpha = fill.slice(fill.lastIndexOf(',') + 1, fill.lastIndexOf(')'))
  return Number.parseFloat(alpha)
}

/** `rgba(r, g, b, a)` → its `[r, g, b]` triple. */
function rgbOf(fill: string): number[] {
  return fill
    .slice(fill.indexOf('(') + 1, fill.lastIndexOf(')'))
    .split(',')
    .slice(0, 3)
    .map(part => Number.parseInt(part.trim(), 10))
}

describe('activityCellFill', () => {
  it('gives an empty period no fill at all, so it cannot look like a loss', () => {
    expect(activityCellFill(bucket({ games: 0, wins: 0, winRate: null }), 8)).toBeNull()
  })

  it('gives a period that lost everything a real fill, on the loss hue', () => {
    const fill = activityCellFill(bucket({ games: 3, wins: 0, winRate: 0 }), 8)
    expect(fill).not.toBeNull()
    expect(rgbOf(fill!)).toEqual([...ACTIVITY_LOSS_RGB])
  })

  it('picks the hue off the side of 50%, never blending through the middle', () => {
    expect(rgbOf(activityCellFill(bucket({ games: 4, wins: 3, winRate: 0.75 }), 8)!))
      .toEqual([...ACTIVITY_WIN_RGB])
    expect(rgbOf(activityCellFill(bucket({ games: 4, wins: 1, winRate: 0.25 }), 8)!))
      .toEqual([...ACTIVITY_LOSS_RGB])
    // Exactly 50% counts as the win side — an arbitrary but fixed tiebreak, so a
    // coin-flip period does not flicker between hues as the sample grows.
    expect(rgbOf(activityCellFill(bucket({ games: 4, wins: 2, winRate: 0.5 }), 8)!))
      .toEqual([...ACTIVITY_WIN_RGB])
  })

  it('stays inside the alpha band, so no populated cell is invisible or opaque', () => {
    const weakest = activityCellFill(bucket({ games: 1, wins: 1, winRate: 0.5 }), 100)!
    const strongest = activityCellFill(bucket({ games: 100, wins: 100, winRate: 1 }), 100)!

    expect(alphaOf(weakest)).toBeGreaterThanOrEqual(ACTIVITY_MIN_ALPHA)
    expect(alphaOf(strongest)).toBeLessThanOrEqual(ACTIVITY_MAX_ALPHA)
    expect(alphaOf(weakest)).toBeLessThan(alphaOf(strongest))
  })

  it('reads a busier period as stronger at the same win rate', () => {
    const thin = activityCellFill(bucket({ games: 1, wins: 1, winRate: 1 }), 10)!
    const busy = activityCellFill(bucket({ games: 10, wins: 10, winRate: 1 }), 10)!
    expect(alphaOf(busy)).toBeGreaterThan(alphaOf(thin))
  })

  it('reads a more decided period as stronger at the same volume', () => {
    const flip = activityCellFill(bucket({ games: 10, wins: 5, winRate: 0.5 }), 10)!
    const sweep = activityCellFill(bucket({ games: 10, wins: 10, winRate: 1 }), 10)!
    expect(alphaOf(sweep)).toBeGreaterThan(alphaOf(flip))
  })

  it('pins the per-game series at full strength instead of dividing by one', () => {
    // Every cell of the per-game series holds exactly one game, so volume carries
    // no information there — it must not collapse the whole grid to the weakest
    // alpha, nor divide by a degenerate denominator.
    const fill = activityCellFill(bucket({ games: 1, wins: 1, winRate: 1 }), 1)!
    expect(alphaOf(fill)).toBeCloseTo(ACTIVITY_MAX_ALPHA, 3)
  })

  it('treats a null win rate as empty even if games disagree', () => {
    // Defensive: the wire contract ties the two together, but a fill built from a
    // null rate would be NaN-coloured rather than obviously wrong.
    expect(activityCellFill(bucket({ games: 3, wins: 0, winRate: null }), 8)).toBeNull()
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

  it('reports 0 for a series retention left empty', () => {
    expect(activityMaxGames(series({ buckets: [] }))).toBe(0)
  })
})

describe('activityBucketResult', () => {
  it('says "No games" rather than printing a 0% nobody measured', () => {
    expect(activityBucketResult(bucket({ games: 0, wins: 0, winRate: null }))).toBe('No games')
  })

  it('names a single game by its result instead of a 100% win rate', () => {
    expect(activityBucketResult(bucket({ games: 1, wins: 1, winRate: 1 }))).toBe('Victory')
    expect(activityBucketResult(bucket({ games: 1, wins: 0, winRate: 0 }))).toBe('Defeat')
  })

  it('prints the record and the rate for an aggregated period', () => {
    expect(activityBucketResult(bucket({ games: 4, wins: 3, winRate: 0.75 })))
      .toBe('3W – 1L (75%)')
    // A genuine 0% is printed as such — the fact the empty case must not borrow.
    expect(activityBucketResult(bucket({ games: 3, wins: 0, winRate: 0 })))
      .toBe('0W – 3L (0%)')
  })
})

describe('activityBucketLabel', () => {
  it('labels a patch cell by its patch', () => {
    expect(activityBucketLabel(bucket({ key: '15.14', startUtc: null }), 'patch'))
      .toBe('Patch 15.14')
  })

  it('labels a day cell with the UTC day, not the viewer local one', () => {
    // 23:30Z belongs to the 29th; a local-timezone format would drift it.
    expect(activityBucketLabel(bucket({ startUtc: '2026-07-29T23:30:00Z' }), 'day'))
      .toBe('Jul 29')
  })

  it('labels a week cell as the Monday-to-Sunday span it covers', () => {
    expect(activityBucketLabel(bucket({ key: '2026-07-27', startUtc: '2026-07-27T00:00:00Z' }), 'week'))
      .toBe('Jul 27 – Aug 2')
  })

  it('labels a game cell down to the hour, since a day can hold several', () => {
    expect(activityBucketLabel(bucket({ startUtc: '2026-07-29T18:05:00Z' }), 'game'))
      .toContain('Jul 29')
  })
})

describe('activityCoverageNote', () => {
  it('says what the match-sourced modes cover and why they stop there', () => {
    const note = activityCoverageNote(series(), null)
    expect(note).toContain('All champions')
    expect(note).toContain('Jul 3')
    expect(note).toContain('Jul 29')
    // The retention bound has to be stated, not implied by a short grid.
    expect(note).toMatch(/prune/i)
  })

  it('points an emptied retention window at the patch mode instead of showing nothing', () => {
    const note = activityCoverageNote(
      series({ buckets: [], games: 0, wins: 0, winRate: null, coverageFromUtc: null, coverageToUtc: null }),
      null,
    )
    expect(note).toMatch(/retained window/i)
    expect(note).toContain('Patch')
  })

  it('names the champion the patch mode is scoped to, and that it is not pruned', () => {
    const note = activityCoverageNote(
      series({
        mode: 'patch',
        source: 'aggregates',
        scope: 'champion',
        championId: 157,
        retentionBounded: false,
        coverageFromUtc: null,
        coverageToUtc: null,
        buckets: [
          bucket({ key: '15.13', startUtc: null }),
          bucket({ key: '15.14', startUtc: null }),
        ],
      }),
      'Yasuo',
    )
    expect(note).toContain('Yasuo')
    expect(note).toContain('2 patches')
    expect(note).toMatch(/never pruned/i)
  })

  it('explains an absent signature champion instead of showing an empty grid', () => {
    const note = activityCoverageNote(
      series({
        mode: 'patch',
        source: 'aggregates',
        scope: 'champion',
        championId: null,
        retentionBounded: false,
        coverageFromUtc: null,
        coverageToUtc: null,
        buckets: [],
        games: 0,
        wins: 0,
        winRate: null,
      }),
      null,
    )
    expect(note).toMatch(/no champion is classified as a main/i)
  })
})
