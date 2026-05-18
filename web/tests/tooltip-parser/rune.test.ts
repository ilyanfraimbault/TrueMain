import { describe, expect, it } from 'vitest'
import { parseRuneDescription, prepareRuneHtml } from '~~/shared/utils/tooltip-parser'

// Real shortDesc captured from CDragon perks.json for Lethal Tempo (id 8008).
const LETHAL_TEMPO_SHORT = "Attacking an enemy champion grants you Attack Speed, up to 6 stacks. At max stacks, deal <lol-uikit-tooltipped-keyword key='LinkTooltip_Description_AdaptiveDmg'><font color='#48C4B7'>bonus adaptive damage</font></lol-uikit-tooltipped-keyword> On-Attack."

// Real longDesc captured from CDragon perks.json for Lethal Tempo (id 8008).
const LETHAL_TEMPO_LONG = "Attacking an enemy champion grants you [6% Melee || 4% Ranged] Attack Speed for 6 seconds, up to 6. At max stacks, deal [9 - 30 Melee || 6 - 24 Ranged] <lol-uikit-tooltipped-keyword key='LinkTooltip_Description_AdaptiveDmg'>bonus adaptive damage On-Attack, increased by 1% per 1% Bonus Attack Speed</lol-uikit-tooltipped-keyword>."

describe('prepareRuneHtml', () => {
  it('rewrites <font color="#48C4B7"> to <runeadaptive>', () => {
    const prepared = prepareRuneHtml('<font color="#48C4B7">x</font>')
    expect(prepared).toBe('<runeadaptive>x</runeadaptive>')
  })

  it('strips <font> wrapper for unknown hex (keeps inner text)', () => {
    const prepared = prepareRuneHtml('<font color="#123456">x</font>')
    expect(prepared).toBe('x')
  })

  it('replaces <lol-uikit-tooltipped-keyword key="..."> with <runekeyword> (drops key=)', () => {
    const prepared = prepareRuneHtml("<lol-uikit-tooltipped-keyword key='Foo'>Attack Speed</lol-uikit-tooltipped-keyword>")
    expect(prepared).toBe('<runekeyword>Attack Speed</runekeyword>')
  })

  it('rewrites [X Melee || Y Ranged] into a <rng melee="X" ranged="Y"> marker', () => {
    const prepared = prepareRuneHtml('[6% Melee || 4% Ranged]')
    expect(prepared).toBe('<rng melee="6%" ranged="4%"></rng>')
  })

  it('does not match incomplete brackets like [a melee]', () => {
    expect(prepareRuneHtml('[a melee]')).toBe('[a melee]')
    expect(prepareRuneHtml('[a Melee ||]')).toBe('[a Melee ||]')
  })

  it('preserves multi-token values inside chips', () => {
    const prepared = prepareRuneHtml('[1.5 seconds Melee || 2 seconds Ranged]')
    expect(prepared).toBe('<rng melee="1.5 seconds" ranged="2 seconds"></rng>')
  })
})

describe('parseRuneDescription', () => {
  it('emits adaptive + keyword segments for Lethal Tempo shortDesc', () => {
    const parsed = parseRuneDescription(LETHAL_TEMPO_SHORT)

    // The keyword wraps the font tag, so we get a keyword segment whose
    // children include the adaptive emphasis. Flatten means: the inner text
    // is reachable under both `runekeyword` and `runeadaptive` tags across
    // adjacent segments.
    const tags = parsed.filter(s => s.kind === 'text').map(s => s.kind === 'text' ? s.tag : '')
    expect(tags).toContain('runeadaptive')
  })

  it('emits a meleeRanged segment for each [X Melee || Y Ranged] chip', () => {
    const parsed = parseRuneDescription(LETHAL_TEMPO_LONG)
    const chips = parsed.filter(s => s.kind === 'meleeRanged')
    expect(chips).toHaveLength(2)
    expect(chips[0]).toEqual({ kind: 'meleeRanged', melee: '6%', ranged: '4%' })
    expect(chips[1]).toEqual({ kind: 'meleeRanged', melee: '9 - 30', ranged: '6 - 24' })
  })

  it('emits a runekeyword segment around the bonus-adaptive-damage phrase', () => {
    const parsed = parseRuneDescription(LETHAL_TEMPO_LONG)
    const keywordSegments = parsed.filter(s => s.kind === 'text' && s.tag === 'runekeyword')
    expect(keywordSegments.length).toBeGreaterThanOrEqual(1)
    const combined = keywordSegments.map(s => s.kind === 'text' ? s.text : '').join('')
    expect(combined).toContain('bonus adaptive damage On-Attack')
  })

  it('returns empty document for empty input', () => {
    expect(parseRuneDescription('')).toEqual([])
  })
})
