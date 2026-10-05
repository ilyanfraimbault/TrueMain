import { describe, expect, it } from 'vitest'
import { chordLabel, chordOf, isKnownKey, suggestedChord, TAB_CHORD } from '~/utils/overlay-keys'

const press = (code: string, modifiers: { alt?: boolean, shift?: boolean, ctrl?: boolean, meta?: boolean } = {}) => ({
  code,
  altKey: !!modifiers.alt,
  shiftKey: !!modifiers.shift,
  ctrlKey: !!modifiers.ctrl,
  metaKey: !!modifiers.meta,
})

describe('overlay keys', () => {
  it('records the physical key, not the character it types', () => {
    // On AZERTY, the key left of Z types "a" and is still `KeyQ`.
    expect(chordOf(press('KeyQ'), true)).toEqual({ alt: false, shift: false, ctrl: false, meta: false, tab: true, key: 'KeyQ' })
    expect(chordOf(press('Digit1', { alt: true, shift: true }), false).key).toBe('Digit1')
  })

  it('takes TAB as part of the chord, never as its key', () => {
    expect(chordOf(press('Tab'), true)).toEqual(TAB_CHORD)
  })

  it('knows the keys the shell does', () => {
    expect(isKnownKey('Backquote')).toBe(true)
    expect(isKnownKey('F12')).toBe(true)
    expect(isKnownKey('NumpadEnter')).toBe(false)
  })

  it('labels a chord the way each platform does', () => {
    const chord = suggestedChord('next-item', 'toggle')
    expect(chordLabel(chord, true)).toBe('⌥⇧1')
    expect(chordLabel(chord, false)).toBe('Alt+Shift+1')
    expect(chordLabel(TAB_CHORD, false)).toBe('Tab')
    expect(chordLabel({ ...TAB_CHORD, key: 'Backquote' }, true)).toBe('Tab+`')
  })

  it('offers a different chord to each panel, and TAB to the item value held', () => {
    const keys = (['next-item', 'win-probability', 'stats', 'item-value'] as const).map(panel => suggestedChord(panel, 'toggle').key)
    expect(new Set(keys).size).toBe(4)
    expect(suggestedChord('item-value', 'whileHeld')).toEqual(TAB_CHORD)
  })
})
