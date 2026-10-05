/** Mirrors `OverlayPanel` in `crates/shell-state/src/overlay.rs`: one window each. */
export type OverlayPanel = 'next-item' | 'win-probability' | 'item-value' | 'stats'
export const OVERLAY_PANELS: OverlayPanel[] = ['next-item', 'win-probability', 'stats', 'item-value']

/** What each panel is called and shows, for the player choosing them. */
export const OVERLAY_PANEL_INFO: Record<OverlayPanel, { label: string, description: string }> = {
  'next-item': { label: 'Next item', description: 'The next item to buy, and the gold it still needs.' },
  'win-probability': { label: 'Win probability', description: 'Each side\'s chance to win, from the item-gold gap and the map. The whole game.' },
  'stats': { label: 'Your pace', description: 'CS per minute with its curve, and gold per minute.' },
  'item-value': { label: 'Item value', description: 'What each team\'s items are worth, and each lane\'s gap. While TAB is held by default.' },
}

export type OverlayShow = 'always' | 'whileDead'
export type OverlayAnchor = 'top-left' | 'top-center' | 'top-right' | 'center-left' | 'center-right'

/**
 * Mirrors `Chord` in `crates/shell-state/src/keys.rs`: the modifiers and TAB
 * held, and at most one key, by its `KeyboardEvent.code` — a key position, so
 * the chord holds on any layout.
 */
export interface Chord {
  alt: boolean
  shift: boolean
  ctrl: boolean
  /** ⌘ on macOS, the Windows key on Windows. */
  meta: boolean
  tab: boolean
  key: string | null
}

/** Mirrors `PanelTrigger`: what brings a panel on screen in game. */
export type PanelTrigger =
  | { kind: 'always' }
  | { kind: 'whileHeld', chord: Chord }
  | { kind: 'toggle', chord: Chord, startShown: boolean }

/** Mirrors `PanelSettings`. */
export interface OverlayPanelSettings {
  enabled: boolean
  anchor: OverlayAnchor
  /** Where the panel was dragged, as a fraction of the room the screen leaves around it (0 against the left/top edge, 1 against the right/bottom); wins over `anchor`. */
  custom: { x: number, y: number } | null
  trigger: PanelTrigger
}

/** Mirrors `OverlaySettings`. */
export interface OverlaySettings {
  /** The whole overlay. */
  enabled: boolean
  /** When the next item shows. */
  show: OverlayShow
  /** 0.8 – 1.4, every panel. */
  scale: number
  /** 0.5 – 1, every panel. */
  opacity: number
  nextItem: OverlayPanelSettings
  winProbability: OverlayPanelSettings
  itemValue: OverlayPanelSettings
  stats: OverlayPanelSettings
}

/** The settings key of each panel. */
export const PANEL_KEY = {
  'next-item': 'nextItem',
  'win-probability': 'winProbability',
  'item-value': 'itemValue',
  'stats': 'stats',
} as const satisfies Record<OverlayPanel, keyof OverlaySettings>

/** Mirrors `OverlayView` in `src-tauri/src/overlay/mod.rs`. */
export interface OverlayView {
  settings: OverlaySettings
  /** False where there is no window layer for the overlay (Linux). */
  supported: boolean
  /** The settings page is showing the panels on screen to place them. */
  preview: boolean
  /** The hide/show key, as the player presses it. */
  shortcut: string
  /** A condition the platform puts on the overlay (Windows: no Full Screen). */
  notice: string | null
  /** Each panel's chord, by panel, if it has one. */
  chords: Partial<Record<OverlayPanel, ChordView>>
  /** The warnings come from the player's own League keybindings, not Riot's defaults. */
  ownBinds: boolean
}

/** Mirrors `ChordView`: a chord as the player presses it, and what it also does in game. */
export interface ChordView {
  label: string
  warning: string | null
}

/** Mirrors `TriggerCheck`: the shell's answer about a trigger before it is saved. */
export interface TriggerCheck {
  refusal: string | null
  chord: ChordView | null
}

export const OVERLAY_SCALE = { min: 0.8, max: 1.4 } as const
export const OVERLAY_OPACITY = { min: 0.5, max: 1 } as const

/** What a browser-only `npm run dev` shows: the shell's defaults. */
export const DEV_OVERLAY_VIEW: OverlayView = {
  settings: {
    enabled: true,
    show: 'always',
    scale: 1,
    opacity: 0.95,
    nextItem: { enabled: true, anchor: 'top-right', custom: null, trigger: { kind: 'always' } },
    winProbability: { enabled: true, anchor: 'top-left', custom: null, trigger: { kind: 'always' } },
    itemValue: { enabled: true, anchor: 'top-center', custom: null, trigger: { kind: 'whileHeld', chord: { alt: false, shift: false, ctrl: false, meta: false, tab: true, key: null } } },
    stats: { enabled: true, anchor: 'center-left', custom: null, trigger: { kind: 'always' } },
  },
  supported: true,
  preview: false,
  shortcut: '⌥⇧O',
  notice: null,
  chords: { 'item-value': { label: 'Tab', warning: null } },
  ownBinds: false,
}
