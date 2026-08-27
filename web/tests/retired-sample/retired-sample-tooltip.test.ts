import { describe, expect, it } from 'vitest'
import { retiredSampleTooltip } from '~/utils/retired-sample'

const NOW = new Date('2026-08-24T12:00:00Z')

describe('retiredSampleTooltip', () => {
  it('dates the figure with the day it was last measured', () => {
    expect(retiredSampleTooltip('2026-08-01T12:01:30Z', NOW)).toContain('1 Aug')
  })

  it('carries the year once the measurement is from another one', () => {
    // Same rule as the match-history day headings, so the two read alike.
    expect(retiredSampleTooltip('2024-11-03T09:00:00Z', NOW)).toContain('3 Nov 2024')
  })

  it('says what actually happened, not that data is missing', () => {
    // The figures are a real past measurement, not a gap: the wording has to
    // stop the profile from reading as "no data yet" (which is what it is not)
    // or as a current count (which is what caused the contradiction).
    const tooltip = retiredSampleTooltip('2026-08-01T12:01:30Z', NOW)!
    expect(tooltip).toMatch(/last measured/i)
    expect(tooltip).toMatch(/aged out/i)
  })

  it('returns null rather than an unusable marker when there is no date', () => {
    expect(retiredSampleTooltip(null, NOW)).toBeNull()
    expect(retiredSampleTooltip(undefined, NOW)).toBeNull()
    expect(retiredSampleTooltip('', NOW)).toBeNull()
    expect(retiredSampleTooltip('not-a-date', NOW)).toBeNull()
  })
})
