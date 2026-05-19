import { describe, expect, it } from 'vitest'
import { parseChampionSpell } from '~~/shared/utils/tooltip-parser'

// Real description captured from DDragon v16.10.1 for Yone Q (Mortal Steel).
const YONE_Q = 'Thrusts forward, damaging all enemies in a line.<br><br>On hit, grants a stack of Gathering Storm for a few seconds. At 2 stacks, Mortal Steel dashes Yone forward with a gust of wind knocking enemies <status>Airborne</status>.'

describe('parseChampionSpell', () => {
  it('parses a typical ability with prose + status keyword + breaks', () => {
    const parsed = parseChampionSpell(YONE_Q)
    const tags = parsed.map(s => s.kind === 'text' ? s.tag : s.kind)
    expect(tags).toContain('status')
    expect(tags).toContain('break')

    const statusText = parsed.find(s => s.kind === 'text' && s.tag === 'status')
    expect(statusText?.kind).toBe('text')
    if (statusText?.kind === 'text') expect(statusText.text).toBe('Airborne')
  })

  it('handles plain-text descriptions (no tags at all)', () => {
    const parsed = parseChampionSpell('A simple ability.')
    expect(parsed).toEqual([{ kind: 'text', tag: 'default', text: 'A simple ability.' }])
  })
})
