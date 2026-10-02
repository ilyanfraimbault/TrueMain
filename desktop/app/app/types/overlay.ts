/** Mirrors `OverlayPanel` in `crates/shell-state/src/overlay.rs`: one window each. */
export type OverlayPanel = 'next-item' | 'win-probability' | 'item-value' | 'stats'
export const OVERLAY_PANELS: OverlayPanel[] = ['next-item', 'win-probability', 'stats', 'item-value']

export type OverlayShow = 'always' | 'whileDead'
export type OverlayAnchor = 'top-left' | 'top-center' | 'top-right' | 'center-left' | 'center-right'

/** Mirrors `PanelSettings`. */
export interface OverlayPanelSettings {
  enabled: boolean
  anchor: OverlayAnchor
  /** Where the panel was dragged, as its centre in fractions of the screen; wins over `anchor`. */
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
  /** False where the overlay's windows are not built yet (Windows). */
  supported: boolean
  /** The settings page is showing the panels on screen to place them. */
  preview: boolean
  /** The hide/show key, as the player presses it. */
  shortcut: string
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
}
