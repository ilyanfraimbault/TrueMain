import { describe, expect, it } from 'vitest'
import { mergedPlayerPath } from '~/utils/player-redirect'

describe('mergedPlayerPath', () => {
  it('sends a merged player\'s page to the kept player', () => {
    expect(mergedPlayerPath('peng-dpm', { slug: 'peng' })).toBe('/players/peng')
  })

  it('stays on a page that already is the player\'s, whatever the case', () => {
    expect(mergedPlayerPath('peng', { slug: 'peng' })).toBeNull()
    expect(mergedPlayerPath('Peng', { slug: 'peng' })).toBeNull()
  })

  it('does nothing for an unknown player', () => {
    expect(mergedPlayerPath('nobody', null)).toBeNull()
    expect(mergedPlayerPath('nobody', undefined)).toBeNull()
  })
})
