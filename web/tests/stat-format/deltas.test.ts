import { describe, expect, it } from 'vitest'
import { formatPointDelta, formatSigned } from '~~/shared/utils/deltas'

describe('formatSigned', () => {
  it('signs both directions with a plus and a typographic minus', () => {
    expect(formatSigned(312)).toBe('+312')
    expect(formatSigned(-184)).toBe('−184')
  })

  it('groups thousands in en-US', () => {
    expect(formatSigned(-1204.4)).toBe('−1,204')
  })

  it('keeps the requested decimals', () => {
    expect(formatSigned(0.254, 2)).toBe('+0.25')
    expect(formatSigned(-1.25, 1)).toBe('−1.3')
  })

  it('leaves a difference that rounds to zero unsigned', () => {
    expect(formatSigned(0)).toBe('0')
    expect(formatSigned(0.3)).toBe('0')
    expect(formatSigned(-0.004, 2)).toBe('0.00')
  })
})

describe('formatPointDelta', () => {
  it('writes a rate difference in percentage points', () => {
    expect(formatPointDelta(0.031)).toBe('+3.1 pts')
    expect(formatPointDelta(-0.0125)).toBe('−1.3 pts')
  })

  it('drops the unit, the sign or decimals on request', () => {
    expect(formatPointDelta(0.031, { unit: false })).toBe('+3.1')
    expect(formatPointDelta(-0.031, { signed: false })).toBe('3.1 pts')
    expect(formatPointDelta(0.071, { digits: 0, unit: false })).toBe('+7')
  })

  it('leaves no movement unsigned', () => {
    expect(formatPointDelta(0.0001)).toBe('0.0 pts')
  })
})
