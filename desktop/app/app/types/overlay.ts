/** Mirrors `OverlayPanel` in `crates/shell-state/src/overlay.rs`: one window each. */
export type OverlayPanel = 'next-item' | 'win-probability' | 'item-value' | 'stats'
export const OVERLAY_PANELS: OverlayPanel[] = ['next-item', 'win-probability', 'stats', 'item-value']

/** What each panel is called and shows, for the player choosing them. */
export const OVERLAY_PANEL_INFO: Record<OverlayPanel, { label: string, description: string }> = {
  'next-item': { label: 'Next item', description: 'The next item to buy, and the gold it still needs.' },
  'win-probability': { label: 'Win probability', description: 'Each side\'s chance to win, from the item-gold gap and the map. The whole game.' },
  'stats': { label: 'Your pace', description: 'CS per minute with its curve, and gold per minute.' },
  'item-value': { label: 'Item value', description: 'While TAB is held: what each team\'s items are worth, and each lane\'s gap.' },
}

export type OverlayShow = 'always' | 'whileDead'
export type OverlayAnchor = 'top-left' | 'top-center' | 'top-right' | 'center-left' | 'center-right'

/** Mirrors `PanelSettings`. */
export interface OverlayPanelSettings {
  enabled: boolean
  anchor: OverlayAnchor
  /** Where the panel was dragged, as a fraction of the room the screen leaves around it (0 against the left/top edge, 1 against the right/bottom); wins over `anchor`. */
  custom: { x: number, y: number } | null
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
    nextItem: { enabled: true, anchor: 'top-right', custom: null },
    winProbability: { enabled: true, anchor: 'top-left', custom: null },
    itemValue: { enabled: true, anchor: 'top-center', custom: null },
    stats: { enabled: true, anchor: 'center-left', custom: null },
  },
  supported: true,
  preview: false,
  shortcut: '⌥⇧O',
  notice: null,
}
