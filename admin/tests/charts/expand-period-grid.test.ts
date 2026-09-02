import { describe, expect, it } from 'vitest'
import { expandPeriodGrid } from '~/utils/charts'

// `expandPeriodGrid` rebuilds the periods the candidate-stock payload leaves out
// (#1403). The backend sends only the periods it actually snapshotted, so without
// this the chart would join the two sides of an ingestor outage into a continuous
// curve — the one shape that reads as "nothing happened".
describe('expandPeriodGrid', () => {
  it('fills the hours between two readings', () => {
    expect(expandPeriodGrid(['2026-08-05T09:00:00Z', '2026-08-05T12:00:00Z'], 'hour')).toEqual([
      '2026-08-05T09:00:00Z',
      '2026-08-05T10:00:00Z',
      '2026-08-05T11:00:00Z',
      '2026-08-05T12:00:00Z',
    ])
  })

  it('emits keys in the backend bucket shape, so lookups match', () => {
    // RunTimeBuckets.Format: ISO-8601 UTC, seconds precision, `Z`. A millisecond
    // suffix would miss every measured bucket and blank the whole series.
    expect(expandPeriodGrid(['2026-08-05T09:00:00Z'], 'hour')).toEqual(['2026-08-05T09:00:00Z'])
  })

  it('steps days and weeks, keeping a Monday-based week on Mondays', () => {
    expect(expandPeriodGrid(['2026-08-04T00:00:00Z', '2026-08-06T00:00:00Z'], 'day')).toHaveLength(3)
    expect(expandPeriodGrid(['2026-08-03T00:00:00Z', '2026-08-17T00:00:00Z'], 'week')).toEqual([
      '2026-08-03T00:00:00Z',
      '2026-08-10T00:00:00Z',
      '2026-08-17T00:00:00Z',
    ])
  })

  it('crosses a month boundary', () => {
    expect(expandPeriodGrid(['2026-08-31T00:00:00Z', '2026-09-01T00:00:00Z'], 'day')).toEqual([
      '2026-08-31T00:00:00Z',
      '2026-09-01T00:00:00Z',
    ])
  })

  it('returns an empty grid for an empty series', () => {
    expect(expandPeriodGrid([], 'hour')).toEqual([])
  })

  it('returns the keys untouched when they are not parseable dates', () => {
    expect(expandPeriodGrid(['not-a-date', 'nor-this'], 'day')).toEqual(['not-a-date', 'nor-this'])
  })

  it('is bounded, so a far-apart pair cannot spin', () => {
    expect(expandPeriodGrid(['2000-01-01T00:00:00Z', '2026-01-01T00:00:00Z'], 'hour'))
      .toHaveLength(2000)
  })
})
