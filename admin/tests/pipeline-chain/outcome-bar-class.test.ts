import { describe, expect, it } from 'vitest'
import { outcomeBarClass } from '~/utils/process-chain'

// The fill of a step's segment in an iteration's time bar: the same palette as the
// status icons, so the bar and the counts beside it never disagree.
describe('outcomeBarClass', () => {
  it('paints each outcome in its status colour', () => {
    expect(outcomeBarClass('Success')).toBe('bg-success/60')
    expect(outcomeBarClass('Failed')).toBe('bg-error')
    expect(outcomeBarClass('Abandoned')).toBe('bg-warning')
    expect(outcomeBarClass('Running')).toBe('bg-primary')
  })

  it('falls back to a neutral fill for the outcomes that carry no alarm', () => {
    expect(outcomeBarClass('Cancelled')).toBe('bg-accented')
    expect(outcomeBarClass('notRun')).toBe('bg-accented')
  })
})
