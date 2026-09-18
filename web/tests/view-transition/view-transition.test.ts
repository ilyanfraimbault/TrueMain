import { describe, expect, it } from 'vitest'
import { allowsViewTransition } from '~/utils/view-transition'

/**
 * Which destinations animate (#1621). The rule worth pinning is the champion
 * page's opt-out — it awaits its build summary in setup, and the browser
 * freezes the outgoing frame for the whole wait — and that it is scoped to the
 * champion page itself, not to everything under `/champions`.
 */
describe('allowsViewTransition', () => {
  it('does not animate into a champion page', () => {
    expect(allowsViewTransition({ path: '/champions/ahri' })).toBe(false)
  })

  it('animates into the champion directory', () => {
    expect(allowsViewTransition({ path: '/champions' })).toBe(true)
  })

  it('animates into the tier list, which lives under the same segment', () => {
    expect(allowsViewTransition({ path: '/champions/tierlist' })).toBe(true)
  })

  it('animates into the other sections', () => {
    expect(allowsViewTransition({ path: '/truemains' })).toBe(true)
    expect(allowsViewTransition({ path: '/truemains/Sheiden-1234' })).toBe(true)
    expect(allowsViewTransition({ path: '/about' })).toBe(true)
  })
})
