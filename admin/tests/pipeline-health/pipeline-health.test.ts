import { describe, expect, it } from 'vitest'
import {
  championDataLagLabel,
  formatGapMagnitude,
  ingestionToAnalysisLabel,
  processStatusColor,
} from '~~/shared/utils/pipeline-health'

// The cockpit's job (#1031) is to never make an unmeasured or a healthy signal look like
// the other one. These pin the three places that could get that wrong: a null gap, a gap
// whose sign means the opposite of what it looks like, and a status with no verdict.

describe('formatGapMagnitude', () => {
  it.each([
    [0, '0m'],
    [1, '1m'],
    [59, '59m'],
    [60, '1h'],
    [90, '1h 30m'],
    [1440, '1d'],
    [1500, '1d 1h'],
  ])('sizes %i minutes as %s', (minutes, expected) => {
    expect(formatGapMagnitude(minutes)).toBe(expected)
  })

  it('reports the magnitude regardless of sign, because the caller words the direction', () => {
    expect(formatGapMagnitude(-90)).toBe('1h 30m')
    expect(formatGapMagnitude(90)).toBe('1h 30m')
  })

  it.each([null, undefined, Number.NaN, Number.POSITIVE_INFINITY])(
    'renders %s as an explicit "not measurable" rather than a zero-minute gap',
    (value) => {
      // A 0 here would claim the pipeline is perfectly caught up when the backend in fact
      // had nothing to subtract — the exact "healthy-looking zero" the page must not show.
      expect(formatGapMagnitude(value as number)).toBe('not measurable')
    },
  )
})

describe('championDataLagLabel', () => {
  it('reads a negative gap as caught up, because the backend computes newestMatch - lastAggregation', () => {
    expect(championDataLagLabel(-120)).toBe('caught up (2h ahead)')
  })

  it('reads zero as caught up rather than as a lag', () => {
    expect(championDataLagLabel(0)).toBe('caught up (0m ahead)')
  })

  it('reads a positive gap as the aggregation trailing the corpus', () => {
    expect(championDataLagLabel(180)).toBe('3h behind')
  })

  it('passes an unmeasurable gap through untouched', () => {
    expect(championDataLagLabel(null)).toBe('not measurable')
  })
})

describe('ingestionToAnalysisLabel', () => {
  it('reads a positive gap as the healthy direction: analysis finished after ingestion', () => {
    expect(ingestionToAnalysisLabel(45)).toBe('analysis 45m after ingestion')
  })

  it('reads a negative gap as analysis trailing', () => {
    expect(ingestionToAnalysisLabel(-45)).toBe('analysis 45m behind ingestion')
  })

  it('passes an unmeasurable gap through untouched', () => {
    expect(ingestionToAnalysisLabel(undefined)).toBe('not measurable')
  })
})

describe('processStatusColor', () => {
  it.each([
    ['Success', 'success'],
    ['Failed', 'error'],
    ['Abandoned', 'warning'],
    ['Running', 'info'],
  ])('paints %s as %s', (status, expected) => {
    expect(processStatusColor(status)).toBe(expected)
  })

  it('paints Missing as neutral — never having run is unmeasured, not a warning', () => {
    expect(processStatusColor('Missing')).toBe('neutral')
  })

  it('falls back to neutral for a status the backend adds later', () => {
    expect(processStatusColor('SomethingNew')).toBe('neutral')
  })
})
