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

    // After the stat-label look-ahead, each `<attention>` segment is retagged
    // to match its stat color (AD → attentionad, Health → attentionhealth,
    // Ability Haste → attentionhaste). Passive / inline emphasis tags pass
    // through unchanged.
    expect(tags).toContain('attentionad')
    expect(tags).toContain('attentionhealth')
    expect(tags).toContain('attentionhaste')
    expect(tags).toContain('passive')
    expect(tags).toContain('physicaldamage')
    expect(tags).toContain('scalearmor')
    expect(tags).toContain('speed')
    expect(tags).toContain('break')

    const textOf = (tag: string): string[] =>
      parsed.filter(s => s.kind === 'text' && s.tag === tag).map(s => (s.kind === 'text' ? s.text : ''))

    // Retagged values land in the right buckets
    expect(textOf('attentionad')).toEqual(['40'])
    expect(textOf('attentionhealth')).toEqual(['400'])
    expect(textOf('attentionhaste')).toEqual(['20'])

    // Passive labels in order
    expect(textOf('passive')).toEqual(['Carve', 'Fervor'])

    // Keyword emphases preserved in order
    expect(textOf('physicaldamage')).toEqual(['physical damage', 'physical damage'])
    expect(textOf('scalearmor')).toEqual(['Armor by 6%'])
    expect(textOf('speed')).toEqual(['20 Move Speed'])
  })

  it('keeps bare attention as AD when no recognised stat label follows', () => {
    // Inside passive prose like "<attention>30%</attention> bar" we have no
    // stat keyword nearby — the value should keep the generic AD-orange tint.
    const parsed = parseItemDescription('<passive>Foo <attention>30%</attention> bar</passive>')
    expect(parsed).toEqual([
      { kind: 'text', tag: 'passive', text: 'Foo ' },
      { kind: 'text', tag: 'attention', text: '30%' },
      { kind: 'text', tag: 'passive', text: ' bar' },
    ])
  })

  it('trims leading and trailing structural breaks', () => {
    // Items with no stats produce `<stats></stats><br><br>...` and most
    // descriptions end with trailing `<br><br>` after the last passive.
    // Neither should bleed into the rendered tooltip as whitespace.
    const parsed = parseItemDescription('<mainText><stats></stats><br><br><active>Consume</active><br>Restores health.</mainText>')
    expect(parsed[0]?.kind).toBe('text')
    if (parsed[0]?.kind === 'text') expect(parsed[0].tag).toBe('active')
    expect(parsed[parsed.length - 1]?.kind).not.toBe('break')
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
