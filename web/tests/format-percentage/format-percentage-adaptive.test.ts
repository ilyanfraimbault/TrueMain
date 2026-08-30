import { describe, expect, it } from 'vitest'
import { formatPercentageAdaptive } from '../../shared/utils/ddragon'

/**
 * The item tooltip's slot popularity: a fringe item picked in 0.4% of builds is
 * a real answer, and rounding it to "0%" reads as "never picked". The extra
 * decimal is spent only below 1%, so the common case stays as terse as the rest
 * of the app's percentages.
 */
describe('formatPercentageAdaptive', () => {
  it('rounds to the requested precision at or above one percent', () => {
    expect(formatPercentageAdaptive(0.42)).toBe('42%')
    expect(formatPercentageAdaptive(0.01)).toBe('1%')
  })

  it('keeps one more decimal below one percent, where rounding would print zero', () => {
    expect(formatPercentageAdaptive(0.004)).toBe('0.4%')
    expect(formatPercentageAdaptive(0.0004)).toBe('0.0%')
  })

  it('honours a higher base precision', () => {
    expect(formatPercentageAdaptive(0.4237, 1)).toBe('42.4%')
    expect(formatPercentageAdaptive(0.004, 1)).toBe('0.40%')
  })
})
