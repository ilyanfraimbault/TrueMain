import { describe, expect, it } from 'vitest'
import { nudgeLabel } from '~/utils/scoreboard-geometry'

// The geometry itself is checked against the shell's by `overlay-geometry.test.ts`.
describe('scoreboard nudge', () => {
  it('names the nudge the way the slider moves it', () => {
    expect(nudgeLabel(0)).toBe('None')
    expect(nudgeLabel(-12)).toBe('12 px up')
    expect(nudgeLabel(8)).toBe('8 px down')
  })
})
