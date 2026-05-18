import { describe, expect, it } from 'vitest'
import { parseItemDescription } from '~~/shared/utils/tooltip-parser'

// Real description captured from DDragon v16.10.1 item.json for Black Cleaver (3071).
const BLACK_CLEAVER = '<mainText><stats><attention>40</attention> Attack Damage<br><attention>400</attention> Health<br><attention>20</attention> Ability Haste</stats><br><br><passive>Carve</passive><br>Dealing <physicalDamage>physical damage</physicalDamage> to champions reduces their <scaleArmor>Armor by 6%</scaleArmor> for 6 seconds. (stacks 5 times).<br><br><passive>Fervor</passive><br>Dealing <physicalDamage>physical damage</physicalDamage> grants <speed>20 Move Speed</speed> for 2 seconds.</mainText>'

describe('parseItemDescription', () => {
  it('returns empty document for empty input', () => {
    expect(parseItemDescription('')).toEqual([])
  })

  it('parses a typical item with stats + two passives', () => {
    const parsed = parseItemDescription(BLACK_CLEAVER)
    const tags = parsed.map(s => s.kind === 'text' ? s.tag : s.kind)

    // Should see the structural <stats> wrapper, four <attention> values
    // (40, 400, 20 plus the inner stat labels), the two <passive> labels
    // (Carve, Fervor), and the inline <physicalDamage>/<scaleArmor>/<speed>
    // emphasis tags.
    expect(tags).toContain('attention')
    expect(tags).toContain('passive')
    expect(tags).toContain('physicaldamage')
    expect(tags).toContain('scalearmor')
    expect(tags).toContain('speed')
    expect(tags).toContain('break')

    const textOf = (tag: string): string[] =>
      parsed.filter(s => s.kind === 'text' && s.tag === tag).map(s => (s.kind === 'text' ? s.text : ''))

    // Numeric callouts (<attention> values)
    expect(textOf('attention')).toEqual(['40', '400', '20'])

    // Passive labels in order
    expect(textOf('passive')).toEqual(['Carve', 'Fervor'])

    // Keyword emphases preserved in order
    expect(textOf('physicaldamage')).toEqual(['physical damage', 'physical damage'])
    expect(textOf('scalearmor')).toEqual(['Armor by 6%'])
    expect(textOf('speed')).toEqual(['20 Move Speed'])
  })

  it('flattens nested tags into a single segment list', () => {
    const parsed = parseItemDescription('<passive>Foo <attention>30%</attention> bar</passive>')
    expect(parsed).toEqual([
      { kind: 'text', tag: 'passive', text: 'Foo ' },
      { kind: 'text', tag: 'attention', text: '30%' },
      { kind: 'text', tag: 'passive', text: ' bar' },
    ])
  })

  it('emits unknown tags as text segments with the tag name (renderer falls open)', () => {
    const parsed = parseItemDescription('<weirdtag>hi</weirdtag>')
    expect(parsed).toEqual([{ kind: 'text', tag: 'weirdtag', text: 'hi' }])
  })

  it('renders <br> as a structural break segment', () => {
    const parsed = parseItemDescription('foo<br>bar')
    expect(parsed).toEqual([
      { kind: 'text', tag: 'default', text: 'foo' },
      { kind: 'break' },
      { kind: 'text', tag: 'default', text: 'bar' },
    ])
  })

  it('collapses 3+ adjacent breaks down to 2 (paragraph separator)', () => {
    const parsed = parseItemDescription('a<br><br><br><br>b')
    const breaks = parsed.filter(s => s.kind === 'break')
    expect(breaks).toHaveLength(2)
  })

  it('descends through <mainText> wrapper without emitting a mainText tag', () => {
    const parsed = parseItemDescription('<mainText>hello</mainText>')
    expect(parsed).toEqual([{ kind: 'text', tag: 'default', text: 'hello' }])
  })
})
