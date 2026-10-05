import type { Chord, OverlayPanel, PanelTrigger } from '~/types/overlay'

/**
 * The keys a panel's chord is recorded from (#1915). What a chord may be is
 * the shell's rule (`crates/shell-state/src/keys.rs`, asked through
 * `overlay_check_trigger`); this only turns key presses into a chord.
 */

/** Mirrors `KeyCode`: the `KeyboardEvent.code`s a chord can use, each with its US label. */
const KEY_LABELS: Record<string, string> = {
  ...Object.fromEntries('ABCDEFGHIJKLMNOPQRSTUVWXYZ'.split('').map(letter => [`Key${letter}`, letter])),
  ...Object.fromEntries('0123456789'.split('').map(digit => [`Digit${digit}`, digit])),
  ...Object.fromEntries(Array.from({ length: 12 }, (_, i) => [`F${i + 1}`, `F${i + 1}`])),
  Backquote: '`',
  Minus: '-',
  Equal: '=',
  BracketLeft: '[',
  BracketRight: ']',
  Backslash: '\\',
  IntlBackslash: '<',
  Semicolon: ';',
  Quote: '\'',
  Comma: ',',
  Period: '.',
  Slash: '/',
  Space: 'Space',
  Escape: 'Esc',
  Enter: 'Enter',
}

const MODIFIERS = new Set(['AltLeft', 'AltRight', 'ShiftLeft', 'ShiftRight', 'ControlLeft', 'ControlRight', 'MetaLeft', 'MetaRight'])

export const isModifier = (code: string) => MODIFIERS.has(code)
export const isKnownKey = (code: string) => code in KEY_LABELS

/** The chord a key press makes: its modifiers, TAB if held, and the key. */
export function chordOf(event: Pick<KeyboardEvent, 'altKey' | 'shiftKey' | 'ctrlKey' | 'metaKey' | 'code'>, tab: boolean): Chord {
  return {
    alt: event.altKey,
    shift: event.shiftKey,
    ctrl: event.ctrlKey,
    meta: event.metaKey,
    tab,
    key: event.code === 'Tab' ? null : event.code,
  }
}

/** TAB alone: the item value's default, the game's scoreboard. */
export const TAB_CHORD: Chord = { alt: false, shift: false, ctrl: false, meta: false, tab: true, key: null }

/**
 * The chord offered when a panel is first put on one: Alt+Shift and a digit,
 * away from the game's default binds (the items are on the bare digits).
 * The item value keeps TAB, held.
 */
export function suggestedChord(panel: OverlayPanel, kind: 'whileHeld' | 'toggle'): Chord {
  if (panel === 'item-value' && kind === 'whileHeld') return TAB_CHORD
  const digit = { 'next-item': 1, 'win-probability': 2, 'stats': 3, 'item-value': 4 }[panel]
  return { alt: true, shift: true, ctrl: false, meta: false, tab: false, key: `Digit${digit}` }
}

/** A chord as the player presses it — what the shell answers with, written here for a browser-only `npm run dev`. */
export function chordLabel(chord: Chord, mac: boolean): string {
  const key = chord.key ? (KEY_LABELS[chord.key] ?? chord.key) : null
  if (mac) {
    const symbols = [chord.ctrl && '⌃', chord.alt && '⌥', chord.shift && '⇧', chord.meta && '⌘'].filter(Boolean).join('')
    return symbols + [chord.tab && 'Tab', key].filter(Boolean).join('+')
  }
  return [chord.ctrl && 'Ctrl', chord.alt && 'Alt', chord.shift && 'Shift', chord.meta && 'Win', chord.tab && 'Tab', key].filter(Boolean).join('+')
}

/** The trigger's chord, if it has one. */
export const chordOfTrigger = (trigger: PanelTrigger): Chord | null => (trigger.kind === 'always' ? null : trigger.chord)
