import { describe, expect, it } from 'vitest'
import { formatRetiredSample } from '~/utils/retired-sample'

const NOW = new Date('2026-08-24T12:00:00Z')

describe('formatRetiredSample', () => {
  it('dates the figure with the day it was last measured', () => {
    const result = formatRetiredSample('2026-08-01T12:01:30Z', NOW)
    expect(result?.suffix).toBe('as of 1 Aug')
    expect(result?.tooltip).toContain('1 Aug')
  })

  it('carries the year once the measurement is from another one', () => {
    // Same rule as the match-history day headings, so the two read alike.
    expect(formatRetiredSample('2024-11-03T09:00:00Z', NOW)?.suffix).toBe('as of 3 Nov 2024')
  })

  it('says what actually happened, not that data is missing', () => {
    // The figures are a real past measurement, not a gap: the wording has to
    // stop the profile from reading as "no data yet" (which is what it is not)
    // or as a current count (which is what caused the contradiction).
    const tooltip = formatRetiredSample('2026-08-01T12:01:30Z', NOW)!.tooltip
    expect(tooltip).toMatch(/last measured/i)
    expect(tooltip).toMatch(/aged out/i)
  })

  it('returns null rather than an unusable badge when there is no date', () => {
    expect(formatRetiredSample(null, NOW)).toBeNull()
    expect(formatRetiredSample(undefined, NOW)).toBeNull()
    expect(formatRetiredSample('', NOW)).toBeNull()
    expect(formatRetiredSample('not-a-date', NOW)).toBeNull()
  })
})
