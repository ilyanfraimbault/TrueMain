/** Mirrors `OverlaySettings` in `crates/shell-state/src/overlay.rs`. */
export interface OverlaySettings {
  enabled: boolean
  show: OverlayShow
  anchor: OverlayAnchor
  /** Where the panel was dragged, as its centre in fractions of the screen; wins over `anchor`. */
  custom: { x: number, y: number } | null
  /** 0.8 – 1.4 */
  scale: number
  /** 0.5 – 1 */
  opacity: number
}

export type OverlayShow = 'always' | 'whileDead'
export type OverlayAnchor = 'top-left' | 'top-right' | 'center-left' | 'center-right'

/** Mirrors `OverlayView` in `src-tauri/src/overlay/mod.rs`. */
export interface OverlayView {
  settings: OverlaySettings
  /** False where the overlay's window is not built yet (Windows). */
  supported: boolean
  /** The settings page is showing the panel on screen to place it. */
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
    anchor: 'top-left',
    custom: null,
    scale: 1,
    opacity: 0.95,
  },
  supported: true,
  preview: false,
  shortcut: '⌥⇧O',
}
