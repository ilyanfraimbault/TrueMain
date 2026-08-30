import { describe, expect, it } from 'vitest'
import { runningTotal } from '~/utils/charts'

// `runningTotal` turns the candidate funnel's per-period counters into the
// cumulative curve the outcome chart draws (#1218). The null handling is the
// part worth pinning: a forward-only counter must not accumulate zeros for the
// months before it existed (#924).
describe('runningTotal', () => {
  it('accumulates a plain series', () => {
    expect(runningTotal([1, 2, 3, 4])).toEqual([1, 3, 6, 10])
  })

  it('keeps leading nulls null so the curve starts where measurement started', () => {
    expect(runningTotal([null, null, 5, 3])).toEqual([null, null, 5, 8])
  })

  it('holds the total flat across an unmeasured period rather than resetting it', () => {
    expect(runningTotal([5, null, 3])).toEqual([5, 5, 8])
  })

  it('treats a measured zero as a real zero, not as an absence', () => {
    expect(runningTotal([null, 0, 2])).toEqual([null, 0, 2])
  })

  it('returns nulls throughout when nothing was ever measured', () => {
    expect(runningTotal([null, null])).toEqual([null, null])
  })

  it('is empty for an empty series', () => {
    expect(runningTotal([])).toEqual([])
  })
})
