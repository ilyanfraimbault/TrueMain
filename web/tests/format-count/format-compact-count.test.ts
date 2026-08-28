import { describe, expect, it } from 'vitest'
import { formatCompactCount } from '~~/shared/utils/counts'

/**
 * The homepage hero prints "how much has TrueMain measured", not a live counter.
 * The two properties that matter: it never overstates (a floor, never a round-up),
 * and it renders identically on the server and in the browser — the truemains chip
 * is server-rendered, so a locale-dependent format would hydration-mismatch.
 */
describe('formatCompactCount', () => {
  it('shortens thousands without a decimal above ten', () => {
    expect(formatCompactCount(490_365)).toBe('490k')
    expect(formatCompactCount(41_255)).toBe('41k')
  })

  it('keeps one decimal below ten, where dropping it would lose a tenth of the value', () => {
    expect(formatCompactCount(4_199)).toBe('4.1k')
    expect(formatCompactCount(1_204_886)).toBe('1.2M')
  })

  it('never rounds up: the printed number is always one the data clears', () => {
    expect(formatCompactCount(999_600)).toBe('999k')
    expect(formatCompactCount(1_999_999)).toBe('1.9M')
  })

  it('prints counts under a thousand exactly, grouped in en-US', () => {
    expect(formatCompactCount(0)).toBe('0')
    expect(formatCompactCount(173)).toBe('173')
    expect(formatCompactCount(999)).toBe('999')
    expect(formatCompactCount(1_000)).toBe('1.0k')
  })

  it('renders a missing or nonsensical count as zero rather than NaN', () => {
    expect(formatCompactCount(Number.NaN)).toBe('0')
    expect(formatCompactCount(-5)).toBe('0')
  })
})
