import { describe, expect, it } from 'vitest'
import { formatPercentage, formatPercentageOrDash } from '~~/shared/utils/ddragon'

/**
 * Ban rate (#920) is the first stat the backend can legitimately not know: it is
 * null on every patch older than ban ingestion. `formatPercentage` takes a plain
 * number and would print "NaN%" for those, which reads as a real measurement.
 */
describe('formatPercentageOrDash', () => {
  it('formats a known value exactly like formatPercentage', () => {
    expect(formatPercentageOrDash(0.1234, 0)).toBe(formatPercentage(0.1234, 0))
    expect(formatPercentageOrDash(0.1234, 1)).toBe('12.3%')
  })

  it('renders an em dash for null and undefined', () => {
    expect(formatPercentageOrDash(null, 0)).toBe('—')
    expect(formatPercentageOrDash(undefined, 0)).toBe('—')
  })

  it('keeps a genuine zero distinct from an unknown value', () => {
    // "observed, never banned" and "not observed" are different answers and must
    // not collapse to the same glyph.
    expect(formatPercentageOrDash(0, 0)).toBe('0%')
    expect(formatPercentageOrDash(0, 0)).not.toBe(formatPercentageOrDash(null, 0))
  })
})
