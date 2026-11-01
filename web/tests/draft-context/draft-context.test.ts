import { describe, expect, it } from 'vitest'
import { refinesBeyondMatchup } from '#common/utils/draft-context'

const matchup = {
  position: 'MIDDLE',
  allies: [{ championId: 103, position: 'MIDDLE' }],
  enemies: [{ championId: 238, position: 'MIDDLE' }],
}

describe('refinesBeyondMatchup', () => {
  it('is false for the matchup alone, or no request', () => {
    expect(refinesBeyondMatchup(matchup)).toBe(false)
    expect(refinesBeyondMatchup(null)).toBe(false)
  })

  it('is true once an ally or an enemy off the played role is picked', () => {
    expect(refinesBeyondMatchup({ ...matchup, allies: [...matchup.allies, { championId: 64, position: 'JUNGLE' }] }))
      .toBe(true)
    expect(refinesBeyondMatchup({ ...matchup, enemies: [...matchup.enemies, { championId: 412, position: 'UTILITY' }] }))
      .toBe(true)
  })
})
