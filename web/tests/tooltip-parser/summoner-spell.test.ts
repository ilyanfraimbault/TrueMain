import { describe, expect, it } from 'vitest'
import { parseSummonerSpell } from '~~/shared/utils/tooltip-parser'

// Real description captured from DDragon v16.10.1 summoner.json for Exhaust.
const EXHAUST = 'Slows target enemy champion and reduces their damage dealt.'

describe('parseSummonerSpell', () => {
  it('returns a single default segment for a plain-text description', () => {
    expect(parseSummonerSpell(EXHAUST)).toEqual([
      { kind: 'text', tag: 'default', text: EXHAUST },
    ])
  })

  it('handles multi-line descriptions via <br>', () => {
    const parsed = parseSummonerSpell('Line one.<br>Line two.')
    expect(parsed).toEqual([
      { kind: 'text', tag: 'default', text: 'Line one.' },
      { kind: 'break' },
      { kind: 'text', tag: 'default', text: 'Line two.' },
    ])
  })

  it('returns empty document for empty input', () => {
    expect(parseSummonerSpell('')).toEqual([])
  })
})
