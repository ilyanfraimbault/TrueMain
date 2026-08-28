import { describe, expect, it } from 'vitest'
import { formatCount } from '../../shared/utils/counts'

/**
 * The one place the app's thousands separator is decided. The property that
 * matters is that it does not depend on the environment: several call sites are
 * server-rendered (the leaderboard rows, the match scoreboard, the share cards),
 * so a locale-dependent separator would hydration-mismatch.
 */
describe('formatCount', () => {
  it('groups thousands the en-US way', () => {
    expect(formatCount(1204)).toBe('1,204')
    expect(formatCount(1_204_886)).toBe('1,204,886')
  })

  it('leaves short numbers alone', () => {
    expect(formatCount(0)).toBe('0')
    expect(formatCount(999)).toBe('999')
  })

  it('formats identically to an explicit en-US call, whatever the ambient locale', () => {
    expect(formatCount(1204)).toBe((1204).toLocaleString('en-US'))
  })
})
